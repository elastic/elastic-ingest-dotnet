// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
using System.Buffers;
#else
using System.Collections.Generic;
#endif
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.Bulk;
using Elastic.Ingest.Elasticsearch.Indices;
using static System.Globalization.CultureInfo;
using static Elastic.Ingest.Elasticsearch.IngestChannelStatics;

namespace Elastic.Ingest.Elasticsearch.Serialization;

/// <summary>
/// Provides static factory methods from producing request data for bulk requests.
/// </summary>
public static class BulkRequestDataFactory
{
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
	/// <summary>
	/// Get the NDJSON request body bytes for a list of items with a header factory and body selector.
	/// This is the lightweight overload that does not require <see cref="IngestChannelOptionsBase{TEvent}"/>.
	/// Supports every <see cref="BulkAction"/>, delete operations are written without a body line.
	/// </summary>
	/// <typeparam name="TItem">The item type that drives both the header and body.</typeparam>
	/// <typeparam name="TBody">The type serialized as the document body for each bulk line.</typeparam>
	/// <param name="items">The items to produce bulk NDJSON for.</param>
	/// <param name="serializerOptions">JSON serializer options (should include a JsonSerializerContext for AOT).</param>
	/// <param name="headerFactory">Produces the bulk operation header for each item.</param>
	/// <param name="bodySelector">Extracts the document body to serialize from each item.</param>
	[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Callers provide JsonSerializerOptions with appropriate context")]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode", Justification = "Callers provide JsonSerializerOptions with appropriate context")]
	public static ReadOnlyMemory<byte> GetBytes<TItem, TBody>(
		ReadOnlySpan<TItem> items,
		JsonSerializerOptions serializerOptions,
		Func<TItem, BulkOperationHeader> headerFactory,
		Func<TItem, TBody> bodySelector)
	{
		var typeInfo = (JsonTypeInfo<TBody>)serializerOptions.GetTypeInfo(typeof(TBody));
		var bufferWriter = new ArrayBufferWriter<byte>();
		using var writer = new Utf8JsonWriter(bufferWriter, WriterOptions);
		foreach (var item in items)
		{
			var action = BulkAction.From(headerFactory(item));
			if (action.HasBody)
				BulkNdjsonWriter.Write(bufferWriter, writer, in action, bodySelector(item), typeInfo);
			else
				BulkNdjsonWriter.WritePrefix(bufferWriter, writer, in action);
		}
		return bufferWriter.WrittenMemory;
	}

	/// <summary>
	/// Get the NDJSON request body bytes for a page of <typeparamref name="TEvent"/> events.
	/// </summary>
	/// <typeparam name="TEvent">The type for the event being ingested.</typeparam>
	/// <param name="page">A page of <typeparamref name="TEvent"/> events.</param>
	/// <param name="options">The <see cref="IngestChannelOptionsBase{TEvent}"/> for the channel where the request will be written.</param>
	/// <param name="createHeaderFactory">A function which takes an instance of <typeparamref name="TEvent"/> and produces the operation header containing the action and optional meta data.</param>
	/// <returns>A <see cref="ReadOnlyMemory{T}"/> of <see cref="byte"/> representing the entire request body in NDJSON format.</returns>
	public static ReadOnlyMemory<byte> GetBytes<TEvent>(ArraySegment<TEvent> page,
		IngestChannelOptionsBase<TEvent> options, Func<TEvent, BulkOperationHeader> createHeaderFactory) =>
		GetBytes(page, options, e => BulkAction.From(createHeaderFactory(e)));

	/// <summary>
	/// Get the NDJSON request body bytes for a page of <typeparamref name="TEvent"/> events.
	/// </summary>
	/// <typeparam name="TEvent">The type for the event being ingested.</typeparam>
	/// <param name="page">A page of <typeparamref name="TEvent"/> events.</param>
	/// <param name="options">The <see cref="IngestChannelOptionsBase{TEvent}"/> for the channel where the request will be written.</param>
	/// <param name="createActionFactory">A function which takes an instance of <typeparamref name="TEvent"/> and produces its <see cref="BulkAction"/>.</param>
	/// <returns>A <see cref="ReadOnlyMemory{T}"/> of <see cref="byte"/> representing the entire request body in NDJSON format.</returns>
	[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "We always provide a static JsonTypeInfoResolver")]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode", Justification = "We always provide a static JsonTypeInfoResolver")]
	public static ReadOnlyMemory<byte> GetBytes<TEvent>(ArraySegment<TEvent> page,
		IngestChannelOptionsBase<TEvent> options, Func<TEvent, BulkAction> createActionFactory)
	{
		// ArrayBufferWriter inserts comma's when serializing multiple times
		// Hence the Reset() after every writer segment in BulkNdjsonWriter as advised on this feature request
		// https://github.com/dotnet/runtime/issues/82314
		var bufferWriter = new ArrayBufferWriter<byte>();
		using var writer = new Utf8JsonWriter(bufferWriter, WriterOptions);
		foreach (var @event in page.AsSpan())
		{
			var action = createActionFactory(@event);
			if (!BulkNdjsonWriter.WritePrefix(bufferWriter, writer, in action)) continue;

			if (options.EventWriter?.WriteToArrayBuffer != null)
				options.EventWriter.WriteToArrayBuffer(bufferWriter, @event);
			else
			{
				JsonSerializer.Serialize(writer, @event, options.SerializerOptions);
				writer.Flush();
				writer.Reset();
			}
			BulkNdjsonWriter.WriteSuffix(bufferWriter, in action);
		}
		return bufferWriter.WrittenMemory;
	}
#endif

	/// <summary>
	/// Asynchronously write the NDJSON request body for a page of <typeparamref name="TEvent"/> events to <see cref="Stream"/>.
	/// </summary>
	/// <typeparam name="TEvent">The type for the event being ingested.</typeparam>
	/// <param name="page">A page of <typeparamref name="TEvent"/> events.</param>
	/// <param name="stream">The target <see cref="Stream"/> for the request.</param>
	/// <param name="options">The <see cref="IngestChannelOptionsBase{TEvent}"/> for the channel where the request will be written.</param>
	/// <param name="createHeaderFactory">A function which takes an instance of <typeparamref name="TEvent"/> and produces the operation header containing the action and optional meta data.</param>
	/// <param name="ctx">The cancellation token to cancel operation.</param>
	/// <returns></returns>
	public static Task WriteBufferToStreamAsync<TEvent>(ArraySegment<TEvent> page, Stream stream,
		IngestChannelOptionsBase<TEvent> options, Func<TEvent, BulkOperationHeader> createHeaderFactory,
		CancellationToken ctx = default) =>
		WriteBufferToStreamAsync(page, stream, options, e => BulkAction.From(createHeaderFactory(e)), ctx);

	/// <summary>
	/// Asynchronously write the NDJSON request body for a page of <typeparamref name="TEvent"/> events to <see cref="Stream"/>.
	/// </summary>
	/// <typeparam name="TEvent">The type for the event being ingested.</typeparam>
	/// <param name="page">A page of <typeparamref name="TEvent"/> events.</param>
	/// <param name="stream">The target <see cref="Stream"/> for the request.</param>
	/// <param name="options">The <see cref="IngestChannelOptionsBase{TEvent}"/> for the channel where the request will be written.</param>
	/// <param name="createActionFactory">A function which takes an instance of <typeparamref name="TEvent"/> and produces its <see cref="BulkAction"/>.</param>
	/// <param name="ctx">The cancellation token to cancel operation.</param>
	/// <returns></returns>
	public static async Task WriteBufferToStreamAsync<TEvent>(ArraySegment<TEvent> page, Stream stream,
		IngestChannelOptionsBase<TEvent> options, Func<TEvent, BulkAction> createActionFactory,
		CancellationToken ctx = default)
	{
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
		var items = page;
#else
		// needs cast prior to netstandard2.0
		IReadOnlyList<TEvent> items = page;
#endif
		using var actionWriter = new StreamActionWriter();
		// for is okay on ArraySegment, foreach performs bad:
		// https://antao-almada.medium.com/how-to-use-span-t-and-memory-t-c0b126aae652
		// ReSharper disable once ForCanBeConvertedToForeach
		for (var i = 0; i < items.Count; i++)
		{
			var @event = items[i];
			if (@event == null) continue;
			await WriteEventToStreamAsync(stream, @event, createActionFactory(@event), options, actionWriter, ctx).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Writes the NDJSON representation of a single event (action/meta line + source line) to <paramref name="stream"/>.
	/// This is the canonical per-event serialization path; both the bulk export and the size-measurement code call it
	/// to guarantee byte-identical output.
	/// </summary>
	[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "We always provide a static JsonTypeInfoResolver")]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode", Justification = "We always provide a static JsonTypeInfoResolver")]
	internal static async Task WriteEventToStreamAsync<TEvent>(Stream stream, TEvent @event,
		BulkAction action, IngestChannelOptionsBase<TEvent> options, StreamActionWriter actionWriter, CancellationToken ctx)
	{
		if (!await actionWriter.WritePrefixAsync(stream, action, ctx).ConfigureAwait(false)) return;

		if (options.EventWriter?.WriteToStreamAsync != null)
			await options.EventWriter.WriteToStreamAsync(stream, @event, ctx).ConfigureAwait(false);
		else
			await JsonSerializer.SerializeAsync(stream, @event, options.SerializerOptions, ctx).ConfigureAwait(false);

		await actionWriter.WriteSuffixAsync(stream, action, ctx).ConfigureAwait(false);
	}

	/// <summary>
	/// Create the bulk action with the appropriate operation and meta data for a bulk request targeting an index.
	/// </summary>
	/// <typeparam name="TEvent">The type for the event being ingested.</typeparam>
	/// <param name="event">The <typeparamref name="TEvent"/> for which the action will be produced.</param>
	/// <param name="channelHash">Hash of channel for scripted hash updates</param>
	/// <param name="options">The <see cref="IndexChannelOptions{TEvent}"/> for the channel.</param>
	/// <param name="skipIndexName">Control whether the index name is included in the meta data for the operation.</param>
	public static BulkAction CreateBulkActionForIndex<TEvent>(TEvent @event, string channelHash, IndexChannelOptions<TEvent> options, bool skipIndexName = false)
	{
		var indexTime = options.TimestampLookup?.Invoke(@event) ?? DateTimeOffset.Now;
		if (options.IndexOffset.HasValue) indexTime = indexTime.ToOffset(options.IndexOffset.Value);

		var index = skipIndexName ? null : string.Format(InvariantCulture, options.IndexFormat, indexTime);
		var id = options.BulkOperationIdLookup?.Invoke(@event);
		var hasId = !string.IsNullOrWhiteSpace(id);

		if (options.OperationMode == OperationMode.Index)
			return BulkAction.Index(hasId ? id : null, index);

		if (options.OperationMode == OperationMode.Create)
			return BulkAction.Create(hasId ? id : null, index);

		if (hasId && id != null && (options.BulkUpsertLookup?.Invoke(@event, id) ?? false))
			return BulkAction.Update(id, index);

		if (!string.IsNullOrWhiteSpace(channelHash) && id != null && options.ScriptedHashBulkUpsertLookup is not null)
			return BulkAction.ScriptedHashUpsert(id, options.ScriptedHashBulkUpsertLookup.Invoke(@event, channelHash), index);

		return hasId ? BulkAction.Index(id, index) : BulkAction.Create(null, index);
	}

	/// <summary>
	/// Create the bulk operation header with the appropriate action and meta data for a bulk request targeting an index.
	/// </summary>
	/// <typeparam name="TEvent">The type for the event being ingested.</typeparam>
	/// <param name="event">The <typeparamref name="TEvent"/> for which the header will be produced.</param>
	/// <param name="channelHash">Hash of channel for <see cref="ScriptedHashUpdateOperation"/></param>
	/// <param name="options">The <see cref="IndexChannelOptions{TEvent}"/> for the channel.</param>
	/// <param name="skipIndexName">Control whether the index name is included in the meta data for the operation.</param>
	/// <returns>A <see cref="BulkOperationHeader"/> instance.</returns>
	public static BulkOperationHeader CreateBulkOperationHeaderForIndex<TEvent>(TEvent @event, string channelHash, IndexChannelOptions<TEvent> options, bool skipIndexName = false)
	{
		var indexTime = options.TimestampLookup?.Invoke(@event) ?? DateTimeOffset.Now;
		if (options.IndexOffset.HasValue) indexTime = indexTime.ToOffset(options.IndexOffset.Value);

		var index = skipIndexName ? string.Empty : string.Format(InvariantCulture, options.IndexFormat, indexTime);

		var id = options.BulkOperationIdLookup?.Invoke(@event);

		if (options.OperationMode == OperationMode.Index)
			return skipIndexName
				? !string.IsNullOrWhiteSpace(id) ? new IndexOperation { Id = id } : new IndexOperation()
				: !string.IsNullOrWhiteSpace(id) ? new IndexOperation { Index = index, Id = id } : new IndexOperation { Index = index };

		if (options.OperationMode == OperationMode.Create)
			return skipIndexName
				? !string.IsNullOrWhiteSpace(id) ? new CreateOperation { Id = id } : new CreateOperation()
				: !string.IsNullOrWhiteSpace(id) ? new CreateOperation { Index = index, Id = id } : new CreateOperation { Index = index };

		if (!string.IsNullOrWhiteSpace(id) && id != null && (options.BulkUpsertLookup?.Invoke(@event, id) ?? false))
			return skipIndexName ? new UpdateOperation { Id = id } : new UpdateOperation { Id = id, Index = index };

		if (!string.IsNullOrWhiteSpace(channelHash) && id != null && options.ScriptedHashBulkUpsertLookup is not null)
		{
			var hashInfo = options.ScriptedHashBulkUpsertLookup.Invoke(@event, channelHash);
			return skipIndexName
				? new ScriptedHashUpdateOperation { Id = id, UpdateInformation = hashInfo}
				: new ScriptedHashUpdateOperation { Id = id, Index = index, UpdateInformation  = hashInfo };
		}


		return
			!string.IsNullOrWhiteSpace(id)
				? skipIndexName ? new IndexOperation { Id = id } : new IndexOperation { Index = index, Id = id }
				: skipIndexName ? new CreateOperation() : new CreateOperation { Index = index };
	}
}

