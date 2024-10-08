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
using Elastic.Channels.Diagnostics;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Ingest.Transport;
using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;
using static Elastic.Ingest.Elasticsearch.ElasticsearchChannelStatics;

namespace Elastic.Ingest.Elasticsearch;

/// <summary> TODO </summary>
public enum IndexOp
{
	/// <summary> </summary>
	Index,
	/// <summary> </summary>
	IndexNoParams,
	/// <summary> </summary>
	Create,
	/// <summary> </summary>
	CreateNoParams,
	/// <summary> </summary>
	Delete,
	/// <summary> </summary>
	Update,
}

/// <summary> TODO </summary>
public readonly struct BulkHeader
{

}

/// <summary>
/// An abstract base class for both <see cref="DataStreamChannel{TEvent}"/> and <see cref="IndexChannel{TEvent}"/>
/// <para>Coordinates most of the sending to- and bootstrapping of Elasticsearch</para>
/// </summary>
public abstract partial class ElasticsearchChannelBase<TEvent, TChannelOptions>
	: TransportChannelBase<TChannelOptions, TEvent, BulkResponse, BulkResponseItem>
	where TChannelOptions : ElasticsearchChannelOptionsBase<TEvent>
{

#if NETSTANDARD2_1_OR_GREATER
	/// <summary>
	/// Get the NDJSON request body bytes for a page of <typeparamref name="TEvent"/> events.
	/// </summary>
	/// <param name="page">A page of <typeparamref name="TEvent"/> events.</param>
	/// <param name="options">The <see cref="ElasticsearchChannelOptionsBase{TEvent}"/> for the channel where the request will be written.</param>
	/// <param name="createHeaderFactory">A function which takes an instance of <typeparamref name="TEvent"/> and produces the operation header containing the action and optional meta data.</param>
	/// <returns>A <see cref="ReadOnlyMemory{T}"/> of <see cref="byte"/> representing the entire request body in NDJSON format.</returns>
	public ReadOnlyMemory<byte> GetBytes(ArraySegment<TEvent> page,
		ElasticsearchChannelOptionsBase<TEvent> options, Func<TEvent, BulkOperationHeader> createHeaderFactory)
	{
		// ArrayBufferWriter inserts comma's when serializing multiple times
		// Hence the multiple writer.Resets() as advised on this feature request
		// https://github.com/dotnet/runtime/issues/82314
		var bufferWriter = new ArrayBufferWriter<byte>();
		using var writer = new Utf8JsonWriter(bufferWriter, WriterOptions);
		foreach (var @event in page.AsSpan())
		{
			var indexHeader = createHeaderFactory(@event);
			JsonSerializer.Serialize(writer, indexHeader, indexHeader.GetType(), SerializerOptions);
			bufferWriter.Write(LineFeed);
			writer.Reset();

			if (indexHeader is UpdateOperation)
			{
				bufferWriter.Write(DocUpdateHeaderStart);
				writer.Reset();
			}

			if (options.EventWriter?.WriteToArrayBuffer != null)
				options.EventWriter.WriteToArrayBuffer(bufferWriter, @event);
			else
				JsonSerializer.Serialize(writer, @event, SerializerOptions);
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

	/// <summary> TODO </summary>
	protected abstract IndexOp GetIndexOp(TEvent @event);

	/// <summary>
	///
	/// </summary>
	/// <param name="header"></param>
	protected abstract void MutateHeader(ref readonly BulkHeader header);

	/// <summary>
	/// Asynchronously write the NDJSON request body for a page of <typeparamref name="TEvent"/> events to <see cref="Stream"/>.
	/// </summary>
	/// <param name="page">A page of <typeparamref name="TEvent"/> events.</param>
	/// <param name="stream">The target <see cref="Stream"/> for the request.</param>
	/// <param name="options">The <see cref="ElasticsearchChannelOptionsBase{TEvent}"/> for the channel where the request will be written.</param>
	/// <param name="ctx">The cancellation token to cancel operation.</param>
	/// <returns></returns>
	public async Task WriteBufferToStreamAsync(
		ArraySegment<TEvent> page, Stream stream, ElasticsearchChannelOptionsBase<TEvent> options, CancellationToken ctx = default)
	{
#if NETSTANDARD2_1_OR_GREATER
		var items = page;
#else
			// needs cast prior to netstandard2.0
			IReadOnlyList<TEvent> items = page;
#endif
		// for is okay on ArraySegment, foreach performs bad:
		// https://antao-almada.medium.com/how-to-use-span-t-and-memory-t-c0b126aae652
		// ReSharper disable once ForCanBeConvertedToForeach
		for (var i = 0; i < items.Count; i++)
		{
			var @event = items[i];
			if (@event == null) continue;

			var op = GetIndexOp(@event);
			switch (op)
			{
				case IndexOp.IndexNoParams:
					await ElasticsearchChannelBase<TEvent, TChannelOptions>.SerializePlainIndexHeaderAsync(stream, ctx).ConfigureAwait(false);
					break;
				case IndexOp.CreateNoParams:
					await SerializePlainCreateHeaderAsync(stream, ctx).ConfigureAwait(false);
					break;
				case IndexOp.Index:
				case IndexOp.Create:
				case IndexOp.Delete:
				case IndexOp.Update:
					var header = new BulkHeader();
					MutateHeader(ref header);
					await SerializeHeaderAsync(stream, ref header, SerializerOptions, ctx).ConfigureAwait(false);
					break;
			}

			await stream.WriteAsync(LineFeed, 0, 1, ctx).ConfigureAwait(false);

			if (op == IndexOp.Update)
				await stream.WriteAsync(DocUpdateHeaderStart, 0, DocUpdateHeaderStart.Length, ctx).ConfigureAwait(false);

			if (options.EventWriter?.WriteToStreamAsync != null)
				await options.EventWriter.WriteToStreamAsync(stream, @event, ctx).ConfigureAwait(false);
			else
				await JsonSerializer.SerializeAsync(stream, @event, SerializerOptions, ctx)
					.ConfigureAwait(false);

			if (op == IndexOp.Update)
				await stream.WriteAsync(DocUpdateHeaderEnd, 0, DocUpdateHeaderEnd.Length, ctx).ConfigureAwait(false);

			await stream.WriteAsync(LineFeed, 0, 1, ctx).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Create the bulk operation header with the appropriate action and meta data for a bulk request targeting an index.
	/// </summary>
	/// <param name="event">The <typeparamref name="TEvent"/> for which the header will be produced.</param>
	/// <param name="options">The <see cref="IndexChannelOptions{TEvent}"/> for the channel.</param>
	/// <param name="skipIndexName">Control whether the index name is included in the meta data for the operation.</param>
	/// <returns>A <see cref="BulkOperationHeader"/> instance.</returns>
	public static BulkOperationHeader CreateBulkOperationHeaderForIndex(TEvent @event, IndexChannelOptions<TEvent> options,
		bool skipIndexName = false)
	{
		var indexTime = options.TimestampLookup?.Invoke(@event) ?? DateTimeOffset.Now;
		if (options.IndexOffset.HasValue) indexTime = indexTime.ToOffset(options.IndexOffset.Value);

		var index = skipIndexName ? string.Empty : string.Format(options.IndexFormat, indexTime);

		var id = options.BulkOperationIdLookup?.Invoke(@event);

		if (options.OperationMode == OperationMode.Index)
		{
			return skipIndexName
				? !string.IsNullOrWhiteSpace(id) ? new IndexOperation { Id = id } : new IndexOperation()
				: !string.IsNullOrWhiteSpace(id) ? new IndexOperation { Index = index, Id = id } : new IndexOperation { Index = index };
		}

		if (options.OperationMode == OperationMode.Create)
		{
			return skipIndexName
				? !string.IsNullOrWhiteSpace(id) ? new CreateOperation { Id = id } : new CreateOperation()
				: !string.IsNullOrWhiteSpace(id) ? new CreateOperation { Index = index, Id = id } : new CreateOperation { Index = index };
		}

		if (!string.IsNullOrWhiteSpace(id) && id != null && (options.BulkUpsertLookup?.Invoke(@event, id) ?? false))
			return skipIndexName ? new UpdateOperation { Id = id } : new UpdateOperation { Id = id, Index = index };

		return
			!string.IsNullOrWhiteSpace(id)
				? skipIndexName ? new IndexOperation { Id = id } : new IndexOperation { Index = index, Id = id }
				: skipIndexName ? new CreateOperation() : new CreateOperation { Index = index };
	}
}
