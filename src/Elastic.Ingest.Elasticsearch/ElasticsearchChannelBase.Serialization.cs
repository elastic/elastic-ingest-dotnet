// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// An abstract base class for both <see cref="DataStreamChannel{TEvent}"/> and <see cref="IndexChannel{TEvent}"/>
/// <para>Coordinates most of the sending to- and bootstrapping of Elasticsearch</para>
/// </summary>
public abstract partial class ElasticsearchChannelBase<TEvent, TChannelOptions>
	where TChannelOptions : ElasticsearchChannelOptionsBase<TEvent>
	where TEvent : class
{
#if NET8_0_OR_GREATER
	private static ReadOnlySpan<byte> IndexPrefixBytesSpan => """{"index":{"""u8;
	private static ReadOnlySpan<byte> CreatePrefixBytesSpan => """{"create":{"""u8;
	private static ReadOnlySpan<byte> DeletePrefixBytesSpan => """{"delete":{"""u8;
	private static ReadOnlySpan<byte> UpdatePrefixBytesSpan => """{"update":{"""u8;
	private static ReadOnlySpan<byte> SuffixBytesSpan => """}}"""u8;

	private static ReadOnlySpan<byte> IdPropertyPrefixBytesSpan => "\"_id\":\""u8;
	private static ReadOnlySpan<byte> IndexPropertyPrefixBytesSpan => "\"_index\":\""u8;
	private static ReadOnlySpan<byte> ListExecutedPipelinesPropertyPrefixBytesSpan => "\"list_executed_pipelines\":"u8;
	private static ReadOnlySpan<byte> RequireAliasPropertyPrefixBytesSpan => "\"require_alias\":"u8;
	private static ReadOnlySpan<byte> DyanamicTemplatesPropertyPrefixBytesSpan => "\"dynamic_templates\":"u8;

	private static ReadOnlySpan<byte> TrueBytesSpan => "true"u8;
	private static ReadOnlySpan<byte> FalseBytesSpan => "false"u8;

	private static ReadOnlySpan<byte> DoubleQuote => [(byte)'"'];
	private static ReadOnlySpan<byte> Comma => [(byte)','];

	private static ReadOnlySpan<byte> OpenSquare => [(byte)'['];
	private static ReadOnlySpan<byte> CloseSquare => [(byte)']'];
	private static ReadOnlySpan<byte> OpenCurlyBrace => [(byte)'{'];
	private static ReadOnlySpan<byte> CloseCurlyBrace => [(byte)'}'];
	private static ReadOnlySpan<byte> Colon => [(byte)':'];
#endif

	private static ReadOnlySpan<byte> PlainIndexBytesSpan => """{"index":{}}"""u8;
	private static ReadOnlySpan<byte> PlainCreateBytesSpan => """{"create":{}}"""u8;

#if NETSTANDARD
	private static byte[] PlainIndexBytes => PlainIndexBytesSpan.ToArray();
	private static byte[] PlainCreateBytes => PlainCreateBytesSpan.ToArray();

	private static readonly JsonEncodedText CreateOperation = JsonEncodedText.Encode("create");
	private static readonly JsonEncodedText UpdateOperation = JsonEncodedText.Encode("update");
	private static readonly JsonEncodedText IndexOperation = JsonEncodedText.Encode("index");
	private static readonly JsonEncodedText DeleteOperation = JsonEncodedText.Encode("delete");
	private static readonly JsonEncodedText IdProperty = JsonEncodedText.Encode("_id");
	private static readonly JsonEncodedText IndexProperty = JsonEncodedText.Encode("_index");
	private static readonly JsonEncodedText RequireAliasProperty = JsonEncodedText.Encode("require_alias");
	private static readonly JsonEncodedText ListExecutedPipelinesProperty = JsonEncodedText.Encode("list_executed_pipelines");
	private static readonly JsonEncodedText DynamicTemplatesProperty = JsonEncodedText.Encode("dynamic_templates");
#endif

#if NET8_0_OR_GREATER
	[SkipLocalsInit]
	private static ValueTask SerializeHeaderAsync(Stream stream, HeaderSerializationStrategy operation, ref readonly BulkHeader? header, CancellationToken ctx)
	{
		if (!header.HasValue)
		{
			switch (operation)
			{
				case HeaderSerializationStrategy.Index:
					stream.Write(PlainIndexBytesSpan);
					break;
				case HeaderSerializationStrategy.Create:
					stream.Write(PlainCreateBytesSpan);
					break;
				default:
					throw new ArgumentException($"Expected non null value for {operation}.", nameof(header));
			}

			return ValueTask.CompletedTask;
		}

		Span<byte> buffer = stackalloc byte[256];

		switch (operation)
		{
			case HeaderSerializationStrategy.Index:
				stream.Write(IndexPrefixBytesSpan);
				break;
			case HeaderSerializationStrategy.Create:
				stream.Write(CreatePrefixBytesSpan);
				break;
			case HeaderSerializationStrategy.Delete:
				stream.Write(DeletePrefixBytesSpan);
				break;
			case HeaderSerializationStrategy.Update:
				stream.Write(UpdatePrefixBytesSpan);
				break;
			default:
				throw new ArgumentException($"Unexpected operation {operation}.");
		}

		var propertyCount = 0;
		var headerValue = header.Value;

		if (!string.IsNullOrEmpty(headerValue.Index))
			WriteString(IndexPropertyPrefixBytesSpan, stream, headerValue.Index, buffer, ref propertyCount);

		if (!string.IsNullOrEmpty(headerValue.Id))
			WriteString(IdPropertyPrefixBytesSpan, stream, headerValue.Id, buffer, ref propertyCount);

		if (headerValue.RequireAlias.HasValue && headerValue.RequireAlias.Value)
		{
			WriteTrue(RequireAliasPropertyPrefixBytesSpan, stream, ref propertyCount);
		}
		else if (headerValue.RequireAlias.HasValue && !headerValue.RequireAlias.Value)
		{
			WriteFalse(RequireAliasPropertyPrefixBytesSpan, stream, ref propertyCount);
		}

		if (headerValue.ListExecutedPipelines.HasValue && headerValue.ListExecutedPipelines.Value)
		{
			WriteTrue(ListExecutedPipelinesPropertyPrefixBytesSpan, stream, ref propertyCount);
		}
		else if (headerValue.ListExecutedPipelines.HasValue && !headerValue.ListExecutedPipelines.Value)
		{
			WriteFalse(ListExecutedPipelinesPropertyPrefixBytesSpan, stream, ref propertyCount);
		}

		if (headerValue.DynamicTemplates is not null && headerValue.DynamicTemplates.Count > 0)
		{
			if (propertyCount > 0)
				stream.Write(Comma);

			stream.Write(DyanamicTemplatesPropertyPrefixBytesSpan);
			stream.Write(OpenSquare);

			var entryCount = 0;
			foreach (var (key, value) in headerValue.DynamicTemplates)
			{
				WriteDictionaryEntry(stream, key, value, buffer, ref entryCount);
			}

			stream.Write(CloseSquare);
			propertyCount++;
		}

		static void WriteString(ReadOnlySpan<byte> propertyNamePrefix, Stream stream, string value, Span<byte> buffer, ref int propertyCount)
		{
			if (propertyCount > 0)
				stream.Write(Comma);

			stream.Write(propertyNamePrefix); // This includes the open quotes for the value

			var length = Encoding.UTF8.GetByteCount(value);

			if (length <= 256)
			{
				Encoding.UTF8.TryGetBytes(value, buffer, out var written);
				stream.Write(buffer[..written]);
			}
			else
			{
				var rentedArray = ArrayPool<byte>.Shared.Rent(length);
				Encoding.UTF8.TryGetBytes(value, rentedArray.AsSpan(), out var written);
				stream.Write(buffer[..written]);
				ArrayPool<byte>.Shared.Return(rentedArray);
			}

			stream.Write(DoubleQuote);
			propertyCount++;
		}

		static void WriteTrue(ReadOnlySpan<byte> propertyNamePrefix, Stream stream, ref int propertyCount)
		{
			if (propertyCount > 0)
				stream.Write(Comma);

			stream.Write(propertyNamePrefix);
			stream.Write(TrueBytesSpan);

			propertyCount++;
		}

		static void WriteFalse(ReadOnlySpan<byte> propertyNamePrefix, Stream stream, ref int propertyCount)
		{
			if (propertyCount > 0)
				stream.Write(Comma);

			stream.Write(propertyNamePrefix);
			stream.Write(FalseBytesSpan);

			propertyCount++;
		}

		static void WriteDictionaryEntry(Stream stream, string key, string value, Span<byte> buffer, ref int entryCount)
		{
			if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value) || key.Length == 0 || value.Length == 0)
				return;

			if (entryCount > 0)
				stream.Write(Comma);

			stream.Write(OpenCurlyBrace);
			WriteQuotedStringBytes(stream, key, buffer);
			stream.Write(Colon);
			WriteQuotedStringBytes(stream, value, buffer);
			stream.Write(CloseCurlyBrace);

			entryCount++;
		}

		static void WriteQuotedStringBytes(Stream stream, string value, Span<byte> buffer)
		{
			stream.Write(DoubleQuote);
			var length = Encoding.UTF8.GetByteCount(value);
			if (length <= 256)
			{
				Encoding.UTF8.TryGetBytes(value, buffer, out var written);
				stream.Write(buffer[..written]);
			}
			else
			{
				var rentedArray = ArrayPool<byte>.Shared.Rent(length);
				Encoding.UTF8.TryGetBytes(value, rentedArray.AsSpan(), out var written);
				stream.Write(buffer[..written]);
				ArrayPool<byte>.Shared.Return(rentedArray);
			}
			stream.Write(DoubleQuote);
		}

		stream.Write(SuffixBytesSpan);

		return ValueTask.CompletedTask;
	}
#else
	private static Task SerializeHeaderAsync(Stream stream, HeaderSerializationStrategy operation, ref readonly BulkHeader? header, CancellationToken ctx)
	{
		if (!header.HasValue)
		{
			if (operation != HeaderSerializationStrategy.Index || operation != HeaderSerializationStrategy.Create)
				throw new ArgumentException($"Expected non null value for {operation}.", nameof(header));

			return HandleNullBulkHeaderAsync(stream, operation);
		}

		var operationString = operation switch
		{
			HeaderSerializationStrategy.Create => CreateOperation,
			HeaderSerializationStrategy.Delete => DeleteOperation,
			HeaderSerializationStrategy.Index => IndexOperation,
			HeaderSerializationStrategy.Update => UpdateOperation,
			HeaderSerializationStrategy.IndexNoParams => throw new InvalidOperationException(),
			HeaderSerializationStrategy.CreateNoParams => throw new InvalidOperationException(),
			_ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
		};

		var headerValue = header.Value;

		return SerializeHeaderAsync(stream, operationString, headerValue.Id, headerValue.Index, headerValue.RequireAlias,
			headerValue.ListExecutedPipelines, headerValue.DynamicTemplates, ctx);
	}

	private static async Task HandleNullBulkHeaderAsync(Stream stream, HeaderSerializationStrategy operation)
	{
		switch (operation)
		{
			case HeaderSerializationStrategy.Index:
				await stream.WriteAsync(PlainIndexBytes, 0, PlainCreateBytes.Length).ConfigureAwait(false);
				break;
			case HeaderSerializationStrategy.Create:
				await stream.WriteAsync(PlainCreateBytes, 0, PlainCreateBytes.Length).ConfigureAwait(false);
				break;
		}
	}

	private static async Task SerializeHeaderAsync(Stream stream, JsonEncodedText operation, string? id, string? index, bool? requireAlias,
		bool? listExecutedPipelines, IDictionary<string, string>? dynamicTemplates, CancellationToken ctx)
	{
		var writer = new Utf8JsonWriter(stream, default);

		await using (writer.ConfigureAwait(false))
		{
			writer.WriteStartObject();
			writer.WritePropertyName(operation);
			writer.WriteStartObject();

			if (!string.IsNullOrWhiteSpace(index))
				writer.WriteString(IndexProperty, index);

			if (!string.IsNullOrWhiteSpace(id))
				writer.WriteString(IdProperty, id);

			if (requireAlias.HasValue)
				writer.WriteBoolean(RequireAliasProperty, requireAlias.Value);

			if (listExecutedPipelines.HasValue)
				writer.WriteBoolean(ListExecutedPipelinesProperty, listExecutedPipelines.Value);

			if (dynamicTemplates is not null)
			{
				writer.WritePropertyName(DynamicTemplatesProperty);
				writer.WriteStartArray();

				foreach (var template in dynamicTemplates)
				{
					writer.WriteStartObject();
					writer.WriteString(template.Key, template.Value);
					writer.WriteEndObject();
				}

				writer.WriteEndArray();
			}

			writer.WriteEndObject();
			writer.WriteEndObject();
		}
	}
#endif

#if NET8_0_OR_GREATER
	private static ValueTask SerializePlainIndexHeaderAsync(Stream stream, CancellationToken ctx = default)
	{
		stream.Write(PlainIndexBytesSpan);
		return ValueTask.CompletedTask;
	}
#else
	private static async ValueTask SerializePlainIndexHeaderAsync(Stream stream, CancellationToken ctx) =>
		await stream.WriteAsync(PlainIndexBytes, 0, PlainIndexBytes.Length, ctx).ConfigureAwait(false);
#endif

#if NET8_0_OR_GREATER
	private static ValueTask SerializePlainCreateHeaderAsync(Stream stream, CancellationToken ctx = default)
	{
		stream.Write(PlainCreateBytesSpan);
		return ValueTask.CompletedTask;
	}
#else
	private static async ValueTask SerializePlainCreateHeaderAsync(Stream stream, CancellationToken ctx) =>
		await stream.WriteAsync(PlainCreateBytes, 0, PlainCreateBytes.Length, ctx).ConfigureAwait(false);
#endif
}
