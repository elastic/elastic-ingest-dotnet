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
using Elastic.Ingest.Elasticsearch.Bulk;
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
		// A 429 for the whole request is still a response to retry, with the whole request: RetryAllItems decides on it.
		// Answering false here ended the export before RetryAllItems was asked, and dropped the batch.
		return details.HasSuccessfulStatusCode || details.HttpStatusCode == 429;
	}

	/// Returns the request timeout as the maximum time <see cref="BufferedChannelBase{TChannelOptions, TEvent, TResponse}.WaitForDrainAsync"/>
	/// should wait for pending flushes
	protected override TimeSpan DrainRequestTimeout =>
		Options.Transport.Configuration.RequestTimeout ?? RequestConfiguration.DefaultRequestTimeout;

	/// <summary>
	/// The URL for the bulk request.
	/// </summary>
	protected virtual string BulkPathAndQuery => "_bulk?filter_path=errors,error,items.*.status,items.*.error,items.*.result,items.*._version";

	// BulkPathAndQuery can change at runtime (catalog channels switch index), so the identity variant is derived per export
	private string EffectiveBulkPathAndQuery => Options.ReturnItemIdentity == Track.None ? BulkPathAndQuery : WithItemIdentity(BulkPathAndQuery, Options.ReturnItemIdentity);

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.RetryAllItems"/>
	protected override bool RetryAllItems(BulkResponse response) => response.ApiCallDetails.HttpStatusCode == 429;

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.Zip"/>
	protected override List<(TDocument, BulkResponseItem)> Zip(BulkResponse response, IReadOnlyCollection<TDocument> page)
	{
		// ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
		if (response.Items == null) return new List<(TDocument, BulkResponseItem)>();

		var zipped = new List<(TDocument, BulkResponseItem)>(Math.Min(page.Count, response.Items.Count));
		using var documents = page.GetEnumerator();
		using var items = response.Items.GetEnumerator();
		while (documents.MoveNext() && items.MoveNext())
			zipped.Add((documents.Current, items.Current));
		return zipped;
	}

	/// <summary>
	/// The request asks for the <c>errors</c> flag, and the server answers <c>false</c> when every item succeeded, so there is nothing to match.
	/// A response without the flag is matched item by item.
	/// </summary>
	protected override bool AllItemsSucceeded(BulkResponse response) => response.Errors == false;

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.RetryEvent"/>
	protected override bool RetryEvent((TDocument, BulkResponseItem) item) =>
		RetryStatusCodes.Contains(item.Item2.Status);

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.RejectEvent"/>
	protected override bool RejectEvent((TDocument, BulkResponseItem) item) =>
		item.Item2.Status < 200 || item.Item2.Status > 299;

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
			return transport.RequestAsync<BulkResponse>(HttpMethod.POST, EffectiveBulkPathAndQuery, PostData.ReadOnlyMemory(bytes), ctx);
		}
#endif
		return ExportStreamingAsync(transport, page, ctx);
	}

	private Task<BulkResponse> ExportStreamingAsync(ITransport transport, ArraySegment<TDocument> page, CancellationToken ctx) =>
		transport.RequestAsync<BulkResponse>(new(HttpMethod.POST, EffectiveBulkPathAndQuery),
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
		// documents per sub-batch, so a sub-batch that failed as a whole can stand in for its documents
		var counts = new List<int>(capacity: 4);
		var inSubBatch = 0;

		// eventStream: holds one event's NDJSON at a time — reused across events, bounded by max single-event size.
		// subBatchStream: accumulates one sub-batch body — bounded by maxBytes.
		using var eventStream = new MemoryStream();
		using var subBatchStream = new MemoryStream();
		using var actionWriter = new StreamActionWriter();

		for (var i = 0; i < page.Count; i++)
		{
			var @event = page[i];
			var header = CreateBulkOperationHeader(@event);

			// Serialize this event exactly once into the event temp buffer.
			eventStream.SetLength(0);
			await BulkRequestDataFactory.WriteEventToStreamAsync(eventStream, @event, header, Options, actionWriter, ctx)
				.ConfigureAwait(false);
			var eventBytes = eventStream.Length;

			// Notify callers about events that individually exceed the budget (still exported, best-effort).
			if (eventBytes > maxBytes)
				NotifyItemExceedsBytesBudget(@event, eventBytes);

			// If adding this event would push the sub-batch over the limit, flush first.
			if (subBatchStream.Length > 0 && subBatchStream.Length + eventBytes > maxBytes)
			{
				responses.Add(await SendSubBatchAsync(transport, subBatchStream, ctx).ConfigureAwait(false));
				counts.Add(inSubBatch);
				inSubBatch = 0;
				subBatchStream.SetLength(0);
			}

			// Append this event's bytes to the sub-batch accumulator.
			eventStream.Position = 0;
			await eventStream.CopyToAsync(subBatchStream, ctx).ConfigureAwait(false);
			inSubBatch++;
		}

		if (subBatchStream.Length > 0)
		{
			responses.Add(await SendSubBatchAsync(transport, subBatchStream, ctx).ConfigureAwait(false));
			counts.Add(inSubBatch);
		}

		return MergeSubBatchResponses(responses, counts);
	}

	private Task<BulkResponse> SendSubBatchAsync(ITransport transport, MemoryStream buffer, CancellationToken ctx)
	{
		buffer.TryGetBuffer(out var segment);
		var bytes = new ReadOnlyMemory<byte>(segment.Array, segment.Offset, (int)buffer.Length);
		return transport.RequestAsync<BulkResponse>(HttpMethod.POST, EffectiveBulkPathAndQuery,
			PostData.ReadOnlyMemory(bytes), ctx);
	}

	private static BulkResponse MergeSubBatchResponses(List<BulkResponse> responses, List<int> counts)
	{
		if (responses.Count == 1) return responses[0];

		var rejectedAsAWhole = responses.Count(r => !r.ApiCallDetails.HasSuccessfulStatusCode);
		var anySucceeded = rejectedAsAWhole < responses.Count;

		// Some sub-batches got through and some did not. The page's items line up with its documents by position, so
		// every sub-batch that failed as a whole stands in for its documents with an item of its status code: a 429, 502, 503 or 504
		// is retried for those documents only, anything else (an unretryable status, no answer at all) is rejected for them only. The merged response is carried by a
		// sub-batch that succeeded, which keeps the channel from retrying (or dropping) the documents that did go through.
		if (anySucceeded && rejectedAsAWhole > 0)
		{
			var merged = new List<BulkResponseItem>(counts.Sum());
			for (var i = 0; i < responses.Count; i++)
			{
				var r = responses[i];
				if (r.ApiCallDetails.HasSuccessfulStatusCode)
				{
					if (r.Items != null) merged.AddRange(r.Items);
					continue;
				}

				// No status at all means the request got no answer, so it may or may not have reached the server. That is not a status
				// to retry on (the documents could be indexed twice): status 0 makes them rejected, which reports them to the caller.
				var status = r.ApiCallDetails.HttpStatusCode ?? 0;
				for (var n = 0; n < counts[i]; n++)
					merged.Add(new BulkResponseItem { Action = "index", Status = status });
			}

			var carrierOfSuccess = responses.First(r => r.ApiCallDetails.HasSuccessfulStatusCode);
			carrierOfSuccess.Items = merged;
			// the carrier's own flag says nothing about the sub-batches that failed
			carrierOfSuccess.Errors = true;
			return carrierOfSuccess;
		}

		// Concatenate items in page order — preserves the positional Zip / RetryBuffer contract.
		var combined = new List<BulkResponseItem>(responses.Sum(r => r.Items?.Count ?? 0));
		foreach (var r in responses)
			if (r.Items != null) combined.AddRange(r.Items);

		// Every sub-batch succeeded, or every one failed. Reuse the "worst" real ApiCallDetails as the merged carrier:
		//   429 (retryAll) > any other error > last response.
		// This preserves the Retry / RetryAllItems behaviour for the full item set.
		var carrier = responses.Find(r => r.ApiCallDetails.HttpStatusCode == 429)
			?? responses.Find(r => !r.ApiCallDetails.HasSuccessfulStatusCode)
			?? responses[^1];

		// BulkResponse.Items has a public setter — replace with the combined list.
		carrier.Items = combined;
		// The carrier is one sub-batch's response, so its flag must not hide the items of the others: "no errors" only
		// when every sub-batch said so, "errors" when any did, and unknown otherwise.
		carrier.Errors = responses.All(r => r.Errors == false) ? false : responses.Any(r => r.Errors == true) ? true : null;
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
	/// <see cref="BulkResponse.Items"/> always lines up position by position with <paramref name="documents"/>,
	/// holding the last result for each document.</returns>
	public async Task<BulkResponse> DirectWriteAsync(
		IReadOnlyList<TDocument> documents,
		int retries,
		TimeSpan? backoffPeriod = null,
		CancellationToken ctx = default)
	{
		backoffPeriod ??= TimeSpan.FromSeconds(2);
		var all = documents as TDocument[] ?? documents.ToArray();
		var current = all;
		// maps positions in the current attempt back to positions in the original documents, null means identity
		int[]? map = null;
		BulkResponseItem[]? merged = null;
		BulkResponse response = null!;

		for (var attempt = 0; attempt <= retries; attempt++)
		{
			response = await ExportAsync(Options.Transport, new ArraySegment<TDocument>(current), ctx).ConfigureAwait(false);

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

			// If the HTTP request itself failed, no items to inspect — stop.
			if (!response.ApiCallDetails.HasSuccessfulStatusCode)
				break;

			// Inspect individual items for retryable failures.
			// ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
			if (response.Items == null || response.Items.Count != current.Length)
				break;

			var items = response.Items as IReadOnlyList<BulkResponseItem> ?? response.Items.ToArray();
			merged ??= new BulkResponseItem[all.Length];
			var retryPositions = new List<int>();
			for (var i = 0; i < items.Count; i++)
			{
				merged[map is null ? i : map[i]] = items[i];
				if (RetryStatusCodes.Contains(items[i].Status)) retryPositions.Add(i);
			}

			if (retryPositions.Count == 0 || attempt == retries)
				break;

			var next = new TDocument[retryPositions.Count];
			var nextMap = new int[retryPositions.Count];
			for (var j = 0; j < retryPositions.Count; j++)
			{
				next[j] = current[retryPositions[j]];
				nextMap[j] = map is null ? retryPositions[j] : map[retryPositions[j]];
			}
			current = next;
			map = nextMap;
			await Task.Delay(backoffPeriod.Value, ctx).ConfigureAwait(false);
		}

		// Items always line up positionally with the documents that were passed in.
		if (merged != null) response.Items = merged;

		return response;
	}

	/// <summary>
	/// Asks implementations to create a <see cref="BulkOperationHeader"/> based on the <paramref name="document"/> being exported.
	/// </summary>
	protected abstract BulkAction CreateBulkOperationHeader(TDocument document);

	/// <summary>  </summary>
	protected class HeadIndexTemplateResponse : ElasticsearchResponse { }

	/// <summary>  </summary>
	protected class PutIndexTemplateResponse : ElasticsearchResponse { }

	/// <summary>  </summary>
	protected class PutComponentTemplateResponse : ElasticsearchResponse { }

	/// <summary>  </summary>
	protected class RefreshResponse : ElasticsearchResponse { }
}
