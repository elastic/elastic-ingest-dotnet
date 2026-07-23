// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Channels.Diagnostics;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Ingest.Transport;
using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;
using static Elastic.Ingest.Elasticsearch.IngestChannelStatics;


namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// An abstract base class for both <see cref="DataStreamChannel{TEvent}"/> and <see cref="IndexChannel{TEvent}"/>
/// <para>Coordinates most of the sending to- and bootstrapping of Elasticsearch</para>
/// </summary>
public abstract partial class IngestChannelBase<TDocument, TChannelOptions>
	: TransportChannelBase<TChannelOptions, TDocument, BulkResponse, BulkResponseItem>
	where TChannelOptions : IngestChannelOptionsBase<TDocument>
	where TDocument : class
{
	/// <inheritdoc cref="IngestChannelBase{TEvent,TChannelOptions}"/>
	protected IngestChannelBase(TChannelOptions options, ICollection<IChannelCallbacks<TDocument, BulkResponse>>? callbackListeners)
		: base(options, callbackListeners) { }

	/// <inheritdoc cref="IngestChannelBase{TEvent,TChannelOptions}"/>
	protected IngestChannelBase(TChannelOptions options)
		: base(options) { }

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.Retry"/>
	protected override bool Retry(BulkResponse response)
	{
		var details = response.ApiCallDetails;
		if (!details.HasSuccessfulStatusCode)
			Options.ExportExceptionCallback?.Invoke(new Exception(details.ToString(), details.OriginalException));
		return details.HasSuccessfulStatusCode;
	}

	/// Returns the request timeout as the maximum time <see cref="BufferedChannelBase{TChannelOptions, TEvent, TResponse}.WaitForDrainAsync"/>
	/// should wait for pending flushes
	protected override TimeSpan DrainRequestTimeout =>
		Options.Transport.Configuration.RequestTimeout ?? RequestConfiguration.DefaultRequestTimeout;

	/// <summary>
	/// The URL for the bulk request.
	/// </summary>
	protected virtual string BulkPathAndQuery => "_bulk?filter_path=error,items.*.status,items.*.error,items.*.result,items.*._version";

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.RetryAllItems"/>
	protected override bool RetryAllItems(BulkResponse response) => response.ApiCallDetails.HttpStatusCode == 429;

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.Zip"/>
	protected override List<(TDocument, BulkResponseItem)> Zip(BulkResponse response, IReadOnlyCollection<TDocument> page) =>
		// ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
		response.Items == null
		? new List<(TDocument, BulkResponseItem)>()
		: page.Zip(response.Items, (doc, item) => (doc, item)).ToList();

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.RetryEvent"/>
	protected override bool RetryEvent((TDocument, BulkResponseItem) @event) =>
		RetryStatusCodes.Contains(@event.Item2.Status);

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.RejectEvent"/>
	protected override bool RejectEvent((TDocument, BulkResponseItem) @event) =>
		@event.Item2.Status < 200 || @event.Item2.Status > 299;

	/// <inheritdoc cref="TransportChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.ExportAsync(Elastic.Transport.ITransport,System.ArraySegment{TEvent},System.Threading.CancellationToken)"/>
	protected override Task<BulkResponse> ExportAsync(ITransport transport, ArraySegment<TDocument> page, CancellationToken ctx = default)
	{
		ctx = ctx == default ? TokenSource.Token : ctx;
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
		if (Options.BufferOptions.OutboundBufferMaxBytes is { } maxBytes)
			return ExportWithSubBatchingAsync(transport, page, maxBytes, ctx);

		// Option is obsolete to prevent external users to set it.
#pragma warning disable CS0618
		if (Options.UseReadOnlyMemory)
#pragma warning restore CS0618
		{
			var bytes = BulkRequestDataFactory.GetBytes(page, Options, CreateBulkOperationHeader);
			return transport.RequestAsync<BulkResponse>(HttpMethod.POST, BulkPathAndQuery, PostData.ReadOnlyMemory(bytes), ctx);
		}
#endif
		return ExportStreamingAsync(transport, page, ctx);
	}

	private Task<BulkResponse> ExportStreamingAsync(ITransport transport, ArraySegment<TDocument> page, CancellationToken ctx) =>
		transport.RequestAsync<BulkResponse>(new(HttpMethod.POST, BulkPathAndQuery),
			PostData.StreamHandler(page,
				(_, _) => { /* sync writer not used */ },
				async (b, stream, localCtx) =>
					await BulkRequestDataFactory.WriteBufferToStreamAsync(b, stream, Options, CreateBulkOperationHeader, localCtx)
						.ConfigureAwait(false)),
			ctx);

#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
	private async Task<BulkResponse> ExportWithSubBatchingAsync(
		ITransport transport, ArraySegment<TDocument> page, long maxBytes, CancellationToken ctx)
	{
		var responses = new List<BulkResponse>(capacity: 4);

		// eventStream: holds one event's NDJSON at a time — reused across events, bounded by max single-event size.
		// subBatchStream: accumulates one sub-batch body — bounded by maxBytes.
		using var eventStream = new MemoryStream();
		using var subBatchStream = new MemoryStream();

		for (var i = 0; i < page.Count; i++)
		{
			var @event = page[i];
			var header = CreateBulkOperationHeader(@event);

			// Serialize this event exactly once into the event temp buffer.
			eventStream.SetLength(0);
			await BulkRequestDataFactory.WriteEventToStreamAsync(eventStream, @event, header, Options, ctx)
				.ConfigureAwait(false);
			var eventBytes = eventStream.Length;

			// Notify callers about events that individually exceed the budget (still exported, best-effort).
			if (eventBytes > maxBytes)
				NotifyItemExceedsBytesBudget(@event, eventBytes);

			// If adding this event would push the sub-batch over the limit, flush first.
			if (subBatchStream.Length > 0 && subBatchStream.Length + eventBytes > maxBytes)
			{
				responses.Add(await SendSubBatchAsync(transport, subBatchStream, ctx).ConfigureAwait(false));
				subBatchStream.SetLength(0);
			}

			// Append this event's bytes to the sub-batch accumulator.
			eventStream.Position = 0;
			await eventStream.CopyToAsync(subBatchStream, ctx).ConfigureAwait(false);
		}

		if (subBatchStream.Length > 0)
			responses.Add(await SendSubBatchAsync(transport, subBatchStream, ctx).ConfigureAwait(false));

		return MergeSubBatchResponses(responses);
	}

	private Task<BulkResponse> SendSubBatchAsync(ITransport transport, MemoryStream buffer, CancellationToken ctx)
	{
		buffer.TryGetBuffer(out var segment);
		var bytes = new ReadOnlyMemory<byte>(segment.Array, segment.Offset, (int)buffer.Length);
		return transport.RequestAsync<BulkResponse>(HttpMethod.POST, BulkPathAndQuery,
			PostData.ReadOnlyMemory(bytes), ctx);
	}

	private static BulkResponse MergeSubBatchResponses(List<BulkResponse> responses)
	{
		if (responses.Count == 1) return responses[0];

		// Concatenate items in page order — preserves the positional Zip / RetryBuffer contract.
		var combined = new List<BulkResponseItem>(responses.Sum(r => r.Items?.Count ?? 0));
		foreach (var r in responses)
			if (r.Items != null) combined.AddRange(r.Items);

		// Reuse the "worst" real ApiCallDetails as the merged carrier:
		//   429 (retryAll) > any other error > last response.
		// This preserves the Retry / RetryAllItems behaviour for the full item set.
		var carrier = responses.Find(r => r.ApiCallDetails.HttpStatusCode == 429)
			?? responses.Find(r => !r.ApiCallDetails.HasSuccessfulStatusCode)
			?? responses[^1];

		// BulkResponse.Items has a public setter — replace with the combined list.
		carrier.Items = combined;
		return carrier;
	}
#endif

	/// <summary>
	/// Writes documents directly to Elasticsearch using the <c>_bulk</c> API, bypassing all channel
	/// buffering, batching, and retry mechanics.
	/// <para>This is useful when the caller needs a synchronous (request/response) write — for example,
	/// persisting data in an API handler and returning only after the data is stored.</para>
	/// <para>Always uses <c>_bulk</c>, even for a single document.</para>
	/// </summary>
	/// <param name="documents">One or more documents to index.</param>
	/// <param name="ctx">Optional cancellation token.</param>
	/// <returns>The <see cref="BulkResponse"/> from Elasticsearch.</returns>
	public Task<BulkResponse> DirectWriteAsync(IReadOnlyList<TDocument> documents, CancellationToken ctx = default)
	{
		var page = new ArraySegment<TDocument>(documents as TDocument[] ?? documents.ToArray());
		return ExportAsync(Options.Transport, page, ctx);
	}

	/// <summary>
	/// Writes documents directly to Elasticsearch using the <c>_bulk</c> API, bypassing all channel
	/// buffering, batching, and retry mechanics.
	/// <para>Convenience overload that accepts documents as <c>params</c>.</para>
	/// </summary>
	/// <param name="documents">One or more documents to index.</param>
	public Task<BulkResponse> DirectWriteAsync(params TDocument[] documents) =>
		DirectWriteAsync(documents, CancellationToken.None);

	/// <summary>
	/// Writes documents directly to Elasticsearch using the <c>_bulk</c> API, bypassing all channel
	/// buffering and batching. Automatically retries failed items using the same retry logic as the
	/// buffered channel (retryable status codes: 429, 502, 503, 504).
	/// <para>On each retry, only the failed retryable items are re-sent. Items that are permanently
	/// rejected (non-2xx, non-retryable) are dropped.</para>
	/// </summary>
	/// <param name="documents">One or more documents to index.</param>
	/// <param name="retries">Maximum number of retry attempts for failed items.</param>
	/// <param name="backoffPeriod">
	/// Delay between retry attempts. Defaults to 2 seconds when <c>null</c>.
	/// </param>
	/// <param name="ctx">Optional cancellation token.</param>
	/// <returns>The final <see cref="BulkResponse"/> from Elasticsearch. When retries occurred, the
	/// response items reflect the last attempt for each document.</returns>
	public async Task<BulkResponse> DirectWriteAsync(
		IReadOnlyList<TDocument> documents,
		int retries,
		TimeSpan? backoffPeriod = null,
		CancellationToken ctx = default)
	{
		backoffPeriod ??= TimeSpan.FromSeconds(2);
		var currentDocuments = documents as TDocument[] ?? documents.ToArray();
		BulkResponse response = null!;

		for (var attempt = 0; attempt <= retries; attempt++)
		{
			var page = new ArraySegment<TDocument>(currentDocuments);
			response = await ExportAsync(Options.Transport, page, ctx).ConfigureAwait(false);

			// If the HTTP request itself failed, no items to inspect — stop.
			if (!response.ApiCallDetails.HasSuccessfulStatusCode)
				break;

			// 429 at HTTP level: retry all items.
			if (RetryAllItems(response))
			{
				if (attempt < retries)
				{
					await Task.Delay(backoffPeriod.Value, ctx).ConfigureAwait(false);
					continue;
				}
				break;
			}

			// Inspect individual items for retryable failures.
			if (response.Items == null)
				break;

			var zipped = Zip(response, page);
			var retryItems = zipped
				.Where(t => RetryEvent(t))
				.Select(t => t.Item1)
				.ToArray();

			if (retryItems.Length == 0)
				break;

			if (attempt < retries)
			{
				currentDocuments = retryItems;
				await Task.Delay(backoffPeriod.Value, ctx).ConfigureAwait(false);
			}
		}

		return response;
	}

	/// <summary>
	/// Asks implementations to create a <see cref="BulkOperationHeader"/> based on the <paramref name="document"/> being exported.
	/// </summary>
	protected abstract BulkOperationHeader CreateBulkOperationHeader(TDocument document);

	/// <summary>  </summary>
	protected class HeadIndexTemplateResponse : ElasticsearchResponse { }

	/// <summary>  </summary>
	protected class PutIndexTemplateResponse : ElasticsearchResponse { }

	/// <summary>  </summary>
	protected class PutComponentTemplateResponse : ElasticsearchResponse { }

	/// <summary>  </summary>
	protected class RefreshResponse : ElasticsearchResponse { }
}
