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
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
	/// Only supports <see cref="UpdateOperation"/> (doc_as_upsert) and simple index/create headers.
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
		var bufferWriter = new ArrayBufferWriter<byte>();
		using var writer = new Utf8JsonWriter(bufferWriter, WriterOptions);
		foreach (var item in items)
		{
			var header = headerFactory(item);
			JsonSerializer.Serialize(writer, header, header.GetType(), serializerOptions);
			bufferWriter.Write(LineFeed);
			writer.Reset();

			if (header is UpdateOperation)
			{
				bufferWriter.Write(DocUpdateHeaderStart);
				writer.Reset();
			}

			var body = bodySelector(item);
			JsonSerializer.Serialize(writer, body, serializerOptions);
			writer.Reset();

			if (header is UpdateOperation)
			{
				bufferWriter.Write(DocUpdateHeaderEnd);
				writer.Reset();
			}

			bufferWriter.Write(LineFeed);
			writer.Reset();
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
	[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "We always provide a static JsonTypeInfoResolver")]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode", Justification = "We always provide a static JsonTypeInfoResolver")]
	public static ReadOnlyMemory<byte> GetBytes<TEvent>(ArraySegment<TEvent> page,
		IngestChannelOptionsBase<TEvent> options, Func<TEvent, BulkOperationHeader> createHeaderFactory)
	{
		// ArrayBufferWriter inserts comma's when serializing multiple times
		// Hence the multiple writer.Resets() as advised on this feature request
		// https://github.com/dotnet/runtime/issues/82314
		var bufferWriter = new ArrayBufferWriter<byte>();
		using var writer = new Utf8JsonWriter(bufferWriter, WriterOptions);
		foreach (var @event in page.AsSpan())
		{
			var indexHeader = createHeaderFactory(@event);
			JsonSerializer.Serialize(writer, indexHeader, indexHeader.GetType(), options.SerializerOptions);
			bufferWriter.Write(LineFeed);
			writer.Reset();

			if (indexHeader is UpdateOperation)
			{
				bufferWriter.Write(DocUpdateHeaderStart);
				writer.Reset();
			}
			if (indexHeader is ScriptedHashUpdateOperation hashUpdate)
			{
				bufferWriter.Write(ScriptedHashUpsertStart);
				writer.Reset();
				var field = Encoding.UTF8.GetBytes(hashUpdate.UpdateInformation.Field);
				bufferWriter.Write(field);
				writer.Reset();
				bufferWriter.Write(ScriptedHashUpsertAfterIfCheck);
				writer.Reset();

				bufferWriter.Write(ScriptedHashUpdateScript);
				writer.Reset();
				if (hashUpdate.UpdateInformation.UpdateScript is not null)
				{
					bufferWriter.Write(Encoding.UTF8.GetBytes(hashUpdate.UpdateInformation.UpdateScript));
					writer.Reset();
				}
				else
				{
					bufferWriter.Write(ScriptedHashAfterIfCheckOp);
					writer.Reset();
				}
				var hash = hashUpdate.UpdateInformation.Hash;
				JsonSerializer.Serialize(writer, hash, options.SerializerOptions);

				if (hashUpdate.UpdateInformation.Parameters is not null)
					foreach (var (key, value) in hashUpdate.UpdateInformation.Parameters)
					{
						bufferWriter.Write(ScriptedHashParamComma);
						writer.Reset();
						JsonSerializer.Serialize(writer, key, options.SerializerOptions);
						bufferWriter.Write(ScriptedHashKeySeparator);
						writer.Reset();
						JsonSerializer.Serialize(writer, value, options.SerializerOptions);
					}

				bufferWriter.Write(ScriptHashDocAsParameter);
				writer.Reset();
			}

			if (options.EventWriter?.WriteToArrayBuffer != null)
				options.EventWriter.WriteToArrayBuffer(bufferWriter, @event);
			else
				JsonSerializer.Serialize(writer, @event, options.SerializerOptions);
			writer.Reset();

			if (indexHeader is UpdateOperation)
			{
				bufferWriter.Write(DocUpdateHeaderEnd);
				writer.Reset();
			}
			if (indexHeader is ScriptedHashUpdateOperation)
			{
				bufferWriter.Write(ScriptedHashUpsertEnd);
				writer.Reset();
			}

			bufferWriter.Write(LineFeed);
			writer.Reset();
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
	[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "We always provide a static JsonTypeInfoResolver")]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode", Justification = "We always provide a static JsonTypeInfoResolver")]
	public static async Task WriteBufferToStreamAsync<TEvent>(ArraySegment<TEvent> page, Stream stream,
		IngestChannelOptionsBase<TEvent> options, Func<TEvent, BulkOperationHeader> createHeaderFactory,
		CancellationToken ctx = default)
	{
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
		var items = page;
#else
			// needs cast prior to netstandard2.0
			IReadOnlyList<TEvent> items = page;
#endif
		// for is okay on ArraySegment, foreach performs bad:
		// https://antao-almada.medium.com/how-to-use-span-t-and-memory-t-c0b126aae652
		// TODO move to Memory<byte> overloads for WriteAsync (CA1835)
#pragma warning disable CA1835
		// ReSharper disable once ForCanBeConvertedToForeach
		for (var i = 0; i < items.Count; i++)
		{
			var @event = items[i];
			if (@event == null) continue;

			var indexHeader = createHeaderFactory(@event);
			await JsonSerializer.SerializeAsync(stream, indexHeader, indexHeader.GetType(), options.SerializerOptions, ctx)
				.ConfigureAwait(false);
			await stream.WriteAsync(LineFeed, 0, 1, ctx).ConfigureAwait(false);

			if (indexHeader is UpdateOperation)
				await stream.WriteAsync(DocUpdateHeaderStart, 0, DocUpdateHeaderStart.Length, ctx).ConfigureAwait(false);

			if (indexHeader is ScriptedHashUpdateOperation hashUpdate)
			{
				await stream.WriteAsync(ScriptedHashUpsertStart, 0, ScriptedHashUpsertStart.Length, ctx).ConfigureAwait(false);
				var field = Encoding.UTF8.GetBytes(hashUpdate.UpdateInformation.Field);
				await stream.WriteAsync(field, 0, field.Length, ctx).ConfigureAwait(false);
				await stream.WriteAsync(ScriptedHashUpsertAfterIfCheck, 0, ScriptedHashUpsertAfterIfCheck.Length, ctx).ConfigureAwait(false);

				if (hashUpdate.UpdateInformation.UpdateScript is { } script && !string.IsNullOrWhiteSpace(script))
				{
					var bytes = Encoding.UTF8.GetBytes(script);
					await stream.WriteAsync(bytes, 0, bytes.Length, ctx).ConfigureAwait(false);
				}
				else
					await stream.WriteAsync(ScriptedHashUpdateScript, 0, ScriptedHashUpdateScript.Length, ctx).ConfigureAwait(false);

				await stream.WriteAsync(ScriptedHashAfterIfCheckOp, 0, ScriptedHashAfterIfCheckOp.Length, ctx).ConfigureAwait(false);

				var hash = hashUpdate.UpdateInformation.Hash;
				await JsonSerializer.SerializeAsync(stream, hash, options.SerializerOptions, ctx).ConfigureAwait(false);

				if (hashUpdate.UpdateInformation.Parameters is { } parameters)
					foreach (var kv in parameters)
					{
						await stream.WriteAsync(ScriptedHashParamComma, 0, ScriptedHashParamComma.Length, ctx ).ConfigureAwait(false);
						await JsonSerializer.SerializeAsync(stream, kv.Key, options.SerializerOptions, ctx).ConfigureAwait(false);
						await stream.WriteAsync(ScriptedHashKeySeparator, 0, ScriptedHashKeySeparator.Length, ctx ).ConfigureAwait(false);
						await JsonSerializer.SerializeAsync(stream, kv.Value, options.SerializerOptions, ctx).ConfigureAwait(false);
					}

				await stream.WriteAsync(ScriptHashDocAsParameter, 0, ScriptHashDocAsParameter.Length, ctx).ConfigureAwait(false);
			}

			if (options.EventWriter?.WriteToStreamAsync != null)
				await options.EventWriter.WriteToStreamAsync(stream, @event, ctx).ConfigureAwait(false);
			else
				await JsonSerializer.SerializeAsync(stream, @event, options.SerializerOptions, ctx)
					.ConfigureAwait(false);

			if (indexHeader is UpdateOperation)
				await stream.WriteAsync(DocUpdateHeaderEnd, 0, DocUpdateHeaderEnd.Length, ctx).ConfigureAwait(false);

			if (indexHeader is ScriptedHashUpdateOperation)
				await stream.WriteAsync(ScriptedHashUpsertEnd, 0, ScriptedHashUpsertEnd.Length, ctx).ConfigureAwait(false);

			await stream.WriteAsync(LineFeed, 0, 1, ctx).ConfigureAwait(false);
		}
#pragma warning restore CA1835
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

