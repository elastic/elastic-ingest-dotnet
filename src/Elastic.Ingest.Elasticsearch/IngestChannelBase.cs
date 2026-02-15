// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
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
		@event.Item2.Status < 200 || @event.Item2.Status > 300;

	/// <inheritdoc cref="TransportChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.ExportAsync(Elastic.Transport.ITransport,System.ArraySegment{TEvent},System.Threading.CancellationToken)"/>
	protected override Task<BulkResponse> ExportAsync(ITransport transport, ArraySegment<TDocument> page, CancellationToken ctx = default)
	{
		ctx = ctx == default ? TokenSource.Token : ctx;
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
		// Option is obsolete to prevent external users to set it.
#pragma warning disable CS0618
		if (Options.UseReadOnlyMemory)
#pragma warning restore CS0618
		{
			var bytes = BulkRequestDataFactory.GetBytes(page, Options, CreateBulkOperationHeader);
			return transport.RequestAsync<BulkResponse>(HttpMethod.POST, BulkPathAndQuery, PostData.ReadOnlyMemory(bytes), ctx);
		}
#endif
#pragma warning disable IDE0022 // Use expression body for method
		return transport.RequestAsync<BulkResponse>(new (HttpMethod.POST, BulkPathAndQuery),
			PostData.StreamHandler(page,
				(_, _) =>
				{
					/* NOT USED */
				},
				async (b, stream, localCtx) =>
					await BulkRequestDataFactory.WriteBufferToStreamAsync(b, stream, Options, CreateBulkOperationHeader, localCtx)
						.ConfigureAwait(false))
			, ctx);
#pragma warning restore IDE0022 // Use expression body for method
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
