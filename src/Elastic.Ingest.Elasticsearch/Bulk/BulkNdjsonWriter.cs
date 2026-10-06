// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elastic.Ingest.Elasticsearch.Indices;
using static Elastic.Ingest.Elasticsearch.IngestChannelStatics;

namespace Elastic.Ingest.Elasticsearch.Bulk;

/// <summary>
/// The single place that knows how a bulk operation is framed as NDJSON:
/// action line, optional wrapper start, the document, wrapper end and the trailing line feed.
/// </summary>
internal static class BulkNdjsonWriter
{
	private static readonly JsonEncodedText IndexProp = JsonEncodedText.Encode("_index");
	private static readonly JsonEncodedText IdProp = JsonEncodedText.Encode("_id");
	private static readonly JsonEncodedText RequireAliasProp = JsonEncodedText.Encode("require_alias");
	private static readonly JsonEncodedText DynamicTemplatesProp = JsonEncodedText.Encode("dynamic_templates");
	private static readonly JsonEncodedText IndexOp = JsonEncodedText.Encode("index");
	private static readonly JsonEncodedText CreateOp = JsonEncodedText.Encode("create");
	private static readonly JsonEncodedText UpdateOp = JsonEncodedText.Encode("update");
	private static readonly JsonEncodedText DeleteOp = JsonEncodedText.Encode("delete");

	/// <summary>
	/// Writes the action line and any wrapper that precedes the document.
	/// Returns <c>true</c> when a document and <see cref="WriteSuffix"/> must follow.
	/// </summary>
	public static bool WritePrefix(IBufferWriter<byte> buffer, Utf8JsonWriter writer, in BulkAction action)
	{
		WriteActionLine(writer, in action);
		writer.Flush();
		writer.Reset();
		WriteRaw(buffer, LineFeed);

		switch (action.Kind)
		{
			case BulkActionKind.Delete:
				return false;
			case BulkActionKind.Update:
				WriteRaw(buffer, DocUpdateHeaderStart);
				break;
			case BulkActionKind.ScriptedHashUpsert:
				WriteScriptedHashPrefix(buffer, writer, action.HashUpdate!);
				break;
		}
		return true;
	}

	/// <summary>Writes the wrapper end (if any) and the trailing line feed after a document.</summary>
	public static void WriteSuffix(IBufferWriter<byte> buffer, in BulkAction action)
	{
		switch (action.Kind)
		{
			case BulkActionKind.Update:
				WriteRaw(buffer, DocUpdateHeaderEnd);
				break;
			case BulkActionKind.ScriptedHashUpsert:
				WriteRaw(buffer, ScriptedHashUpsertEnd);
				break;
		}
		WriteRaw(buffer, LineFeed);
	}

	/// <summary>Writes a complete operation (action line, document, suffix) using <paramref name="typeInfo"/> for the document.</summary>
	public static void Write<TBody>(IBufferWriter<byte> buffer, Utf8JsonWriter writer, in BulkAction action,
		TBody body, JsonTypeInfo<TBody> typeInfo)
	{
		if (!WritePrefix(buffer, writer, in action)) return;

		JsonSerializer.Serialize(writer, body, typeInfo);
		writer.Flush();
		writer.Reset();
		WriteSuffix(buffer, in action);
	}

	private static void WriteActionLine(Utf8JsonWriter writer, in BulkAction action)
	{
		writer.WriteStartObject();
		writer.WritePropertyName(action.Kind switch
		{
			BulkActionKind.Index => IndexOp,
			BulkActionKind.Create => CreateOp,
			BulkActionKind.Delete => DeleteOp,
			_ => UpdateOp
		});
		writer.WriteStartObject();
		if (!string.IsNullOrWhiteSpace(action.IndexName))
			writer.WriteString(IndexProp, action.IndexName);
		if (!string.IsNullOrWhiteSpace(action.Id))
			writer.WriteString(IdProp, action.Id);
		if (action.RequireAlias)
			writer.WriteBoolean(RequireAliasProp, true);
		if (action.Kind is BulkActionKind.Index or BulkActionKind.Create && action.DynamicTemplates is { Count: > 0 } templates)
		{
			writer.WritePropertyName(DynamicTemplatesProp);
			writer.WriteStartObject();
			foreach (var kv in templates)
				writer.WriteString(kv.Key, kv.Value);
			writer.WriteEndObject();
		}
		writer.WriteEndObject();
		writer.WriteEndObject();
	}

	private static void WriteScriptedHashPrefix(IBufferWriter<byte> buffer, Utf8JsonWriter writer, HashedBulkUpdate update)
	{
		WriteRaw(buffer, ScriptedHashUpsertStart);
		WriteUtf8(buffer, update.Field);
		WriteRaw(buffer, ScriptedHashUpsertAfterIfCheck);

		if (update.UpdateScript is { } script && !string.IsNullOrWhiteSpace(script))
			WriteUtf8(buffer, script);
		else
			WriteRaw(buffer, ScriptedHashUpdateScript);

		WriteRaw(buffer, ScriptedHashElseBranchStart);
		WriteUtf8(buffer, update.Field);
		WriteRaw(buffer, ScriptedHashElseBranchEnd);

		writer.WriteStringValue(update.Hash);
		writer.Flush();
		writer.Reset();

		if (update.Parameters is { } parameters)
			foreach (var kv in parameters)
			{
				WriteRaw(buffer, ScriptedHashParamComma);
				writer.WriteStringValue(kv.Key);
				writer.Flush();
				writer.Reset();
				WriteRaw(buffer, ScriptedHashKeySeparator);
				writer.WriteStringValue(kv.Value);
				writer.Flush();
				writer.Reset();
			}

		WriteRaw(buffer, ScriptHashDocAsParameter);
	}

	/// <summary>Copies <paramref name="bytes"/> to <paramref name="buffer"/>.</summary>
	internal static void WriteRaw(IBufferWriter<byte> buffer, byte[] bytes)
	{
		bytes.AsSpan().CopyTo(buffer.GetSpan(bytes.Length));
		buffer.Advance(bytes.Length);
	}

	/// <summary>Writes <paramref name="value"/> as raw (unescaped) UTF8, without allocating where the runtime allows.</summary>
	private static void WriteUtf8(IBufferWriter<byte> buffer, string value)
	{
#if NETSTANDARD2_0
		WriteRaw(buffer, Encoding.UTF8.GetBytes(value));
#else
		var span = buffer.GetSpan(Encoding.UTF8.GetMaxByteCount(value.Length));
		buffer.Advance(Encoding.UTF8.GetBytes(value, span));
#endif
	}
}
