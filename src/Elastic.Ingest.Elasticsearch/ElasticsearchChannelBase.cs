// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Ingest.Transport;
using Elastic.Transport;
using static Elastic.Ingest.Elasticsearch.ElasticsearchChannelStatics;

namespace Elastic.Ingest.Elasticsearch
{
	/// <summary>
	/// An abstract base class for both <see cref="DataStreamChannel{TEvent}"/> and <see cref="IndexChannel{TEvent}"/>
	/// <para>Coordinates most of the sending to- and bootstrapping of Elasticsearch</para>
	/// </summary>
	public abstract partial class ElasticsearchChannelBase<TEvent, TChannelOptions>
		: TransportChannelBase<TChannelOptions, TEvent, BulkResponse, BulkResponseItem>
		where TChannelOptions : ElasticsearchChannelOptionsBase<TEvent>
	{
		/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}"/>
		protected ElasticsearchChannelBase(TChannelOptions options) : base(options) { }

		/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.Retry"/>
		protected override bool Retry(BulkResponse response)
		{
			var details = response.ApiCallDetails;
			if (!details.HasSuccessfulStatusCode)
				Options.ExportExceptionCallback?.Invoke(new Exception(details.ToString(), details.OriginalException));
			return details.HasSuccessfulStatusCode;
		}

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

		/// <inheritdoc cref="TransportChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}.Export(Elastic.Transport.HttpTransport,System.ArraySegment{TEvent},System.Threading.CancellationToken)"/>
		protected override Task<BulkResponse> Export(HttpTransport transport, ArraySegment<TEvent> page, CancellationToken ctx = default)
		{
#if NETSTANDARD2_1
			if (Options.UseArrayBuffer)
			{
				var bytes = GetBytes(page);
				return transport.RequestAsync<BulkResponse>(HttpMethod.POST, "/_bulk", PostData.ReadOnlyMemory(bytes), RequestParams, ctx);
			}
#endif
			return transport.RequestAsync<BulkResponse>(HttpMethod.POST, "/_bulk",
				PostData.StreamHandler(page,
					(_, _) =>
					{
						/* NOT USED */
					},
					async (b, stream, ctx) => { await WriteBufferToStreamAsync(b, stream, ctx).ConfigureAwait(false); })
				, RequestParams, ctx);
		}

		/// <summary>
		/// Asks implementations to create a <see cref="BulkOperationHeader"/> based on the <paramref name="event"/> being exported.
		/// </summary>
		protected abstract BulkOperationHeader CreateBulkOperationHeader(TEvent @event);

#if NETSTANDARD2_1_OR_GREATER
		private ReadOnlyMemory<byte> GetBytes(ArraySegment<TEvent> page)
		{
			// ArrayBufferWriter inserts comma's when serializing multiple times
			// Hence the multiple writer.Resets() as advised on this feature request
			// https://github.com/dotnet/runtime/issues/82314
			var bufferWriter = new ArrayBufferWriter<byte>();
			using var writer = new Utf8JsonWriter(bufferWriter, WriterOptions);
			foreach (var @event in page.AsSpan())
			{
				var indexHeader = CreateBulkOperationHeader(@event);
				JsonSerializer.Serialize(writer, indexHeader, indexHeader.GetType(), SerializerOptions);
				bufferWriter.Write(LineFeed);
				writer.Reset();

				if (indexHeader is UpdateOperation)
				{
					bufferWriter.Write(DocUpdateHeaderStart);
					writer.Reset();
				}

				if (Options.EventWriter?.WriteToArrayBuffer != null)
					Options.EventWriter.WriteToArrayBuffer(bufferWriter, @event);
				else
					JsonSerializer.Serialize<TEvent>(writer, @event, SerializerOptions);
				writer.Reset();

				if (indexHeader is UpdateOperation)
				{
					bufferWriter.Write(DocUpdateHeaderEnd);
					writer.Reset();
				}

				bufferWriter.Write(LineFeed);
				writer.Reset();
			}
			return bufferWriter.WrittenMemory;
		}

#endif
		private async Task WriteBufferToStreamAsync(ArraySegment<TEvent> b, Stream stream, CancellationToken ctx)
		{
			// for is okay on ArraySegment, foreach performs bad:
			// https://antao-almada.medium.com/how-to-use-span-t-and-memory-t-c0b126aae652
			// ReSharper disable once ForCanBeConvertedToForeach
#if NETSTANDARD2_1_OR_GREATER
			var items = b;
#else
			// needs cast prior to netstandard2.0
			IReadOnlyList<TEvent> items = b;
#endif
			for (var i = 0; i < items.Count; i++)
			{
				var @event = items[i];
				if (@event == null) continue;

				var indexHeader = CreateBulkOperationHeader(@event);
				await JsonSerializer.SerializeAsync(stream, indexHeader, indexHeader.GetType(), SerializerOptions, ctx)
					.ConfigureAwait(false);
				await stream.WriteAsync(LineFeed, 0, 1, ctx).ConfigureAwait(false);

				if (indexHeader is UpdateOperation)
					await stream.WriteAsync(DocUpdateHeaderStart, 0, DocUpdateHeaderStart.Length, ctx).ConfigureAwait(false);

				if (Options.EventWriter?.WriteToStreamAsync != null)
					await Options.EventWriter.WriteToStreamAsync(stream, @event, ctx).ConfigureAwait(false);
				else
					await JsonSerializer.SerializeAsync<TEvent>(stream, @event, SerializerOptions, ctx)
						.ConfigureAwait(false);

				if (indexHeader is UpdateOperation)
					await stream.WriteAsync(DocUpdateHeaderEnd, 0, DocUpdateHeaderEnd.Length, ctx).ConfigureAwait(false);

				await stream.WriteAsync(LineFeed, 0, 1, ctx).ConfigureAwait(false);
			}
		}

		/// <summary>  </summary>
		protected class HeadIndexTemplateResponse : TransportResponse { }

		/// <summary>  </summary>
		protected class PutIndexTemplateResponse : TransportResponse { }

		/// <summary>  </summary>
		protected class PutComponentTemplateResponse : TransportResponse { }
	}
}
