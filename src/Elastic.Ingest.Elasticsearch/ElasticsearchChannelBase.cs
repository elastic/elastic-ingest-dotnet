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
using static Elastic.Ingest.Elasticsearch.ElasticsearchChannelStatics;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// An abstract base class for both <see cref="DataStreamChannel{TEvent}"/> and <see cref="IndexChannel{TEvent}"/>
/// <para>Coordinates most of the sending to- and bootstrapping of Elasticsearch</para>
/// </summary>
public abstract partial class ElasticsearchChannelBase<TEvent, TChannelOptions>
	: TransportChannelBase<TChannelOptions, TEvent, BulkResponse, BulkResponseItem>
	where TChannelOptions : ElasticsearchChannelOptionsBase<TEvent>
{
	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}"/>
	protected ElasticsearchChannelBase(TChannelOptions options, ICollection<IChannelCallbacks<TEvent, BulkResponse>>? callbackListeners)
		: base(options, callbackListeners) { }

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}"/>
	protected ElasticsearchChannelBase(TChannelOptions options)
		: base(options) { }

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.Retry"/>
	protected override bool Retry(BulkResponse response)
	{
		var details = response.ApiCallDetails;
		if (!details.HasSuccessfulStatusCode)
			Options.ExportExceptionCallback?.Invoke(new Exception(details.ToString(), details.OriginalException));
		return details.HasSuccessfulStatusCode;
	}

	/// <summary>
	/// The URL for the bulk request.
	/// </summary>
	protected virtual string BulkUrl => "/_bulk";

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.RetryAllItems"/>
	protected override bool RetryAllItems(BulkResponse response) => response.ApiCallDetails.HttpStatusCode == 429;

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.Zip"/>
	protected override List<(TEvent, BulkResponseItem)> Zip(BulkResponse response, IReadOnlyCollection<TEvent> page) =>
		page.Zip(response.Items, (doc, item) => (doc, item)).ToList();

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.RetryEvent"/>
	protected override bool RetryEvent((TEvent, BulkResponseItem) @event) =>
		RetryStatusCodes.Contains(@event.Item2.Status);

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.RejectEvent"/>
	protected override bool RejectEvent((TEvent, BulkResponseItem) @event) =>
		@event.Item2.Status < 200 || @event.Item2.Status > 300;

	/// <inheritdoc cref="TransportChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.ExportAsync(Elastic.Transport.ITransport,System.ArraySegment{TEvent},System.Threading.CancellationToken)"/>
	protected override Task<BulkResponse> ExportAsync(ITransport transport, ArraySegment<TEvent> page, CancellationToken ctx = default)
	{
		ctx = ctx == default ? TokenSource.Token : ctx;
#if NETSTANDARD2_1
		// Option is obsolete to prevent external users to set it.
#pragma warning disable CS0618
		if (Options.UseReadOnlyMemory)
#pragma warning restore CS0618
		{
			var bytes = BulkRequestDataFactory.GetBytes(page, Options, CreateBulkOperationHeader);
			return transport.RequestAsync<BulkResponse>(HttpMethod.POST, BulkUrl, PostData.ReadOnlyMemory(bytes), RequestParams, ctx);
		}
#endif
#pragma warning disable IDE0022 // Use expression body for method
		return transport.RequestAsync<BulkResponse>(HttpMethod.POST, BulkUrl,
			PostData.StreamHandler(page,
				(_, _) =>
				{
					/* NOT USED */
				},
				async (b, stream, ctx) => { await BulkRequestDataFactory.WriteBufferToStreamAsync(b, stream, Options, CreateBulkOperationHeader, ctx).ConfigureAwait(false); })
			, RequestParams, ctx);
#pragma warning restore IDE0022 // Use expression body for method
	}

	/// <summary>
	/// Asks implementations to create a <see cref="BulkOperationHeader"/> based on the <paramref name="event"/> being exported.
	/// </summary>
	protected abstract BulkOperationHeader CreateBulkOperationHeader(TEvent @event);

	/// <summary>  </summary>
	protected class HeadIndexTemplateResponse : ElasticsearchResponse { }

	/// <summary>  </summary>
	protected class PutIndexTemplateResponse : ElasticsearchResponse { }

	/// <summary>  </summary>
	protected class PutComponentTemplateResponse : ElasticsearchResponse { }
}
