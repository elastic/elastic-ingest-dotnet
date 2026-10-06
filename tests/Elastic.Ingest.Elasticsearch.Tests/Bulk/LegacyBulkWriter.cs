// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Buffers;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elastic.Ingest.Elasticsearch.Bulk;
using Elastic.Ingest.Elasticsearch.Serialization;

namespace Elastic.Ingest.Elasticsearch.Tests.Bulk;

/// <summary>
/// A frozen, independent copy of the NDJSON framing that shipped before the shared <c>BulkNdjsonWriter</c>
/// (BulkRequestDataFactory at 0.51.1) using only public API. It is the reference model the new writer is compared against,
/// do not "improve" it: its value is that it does not share code with the implementation under test.
/// Like the original it does not understand delete, it always writes a body line.
/// </summary>
public static class LegacyBulkWriter
{
	private static readonly byte[] LineFeed = [(byte)'\n'];
	private static readonly byte[] DocUpdateHeaderStart = "{\"doc_as_upsert\": true, \"doc\": "u8.ToArray();
	private static readonly byte[] DocUpdateHeaderEnd = " }"u8.ToArray();
	private static readonly byte[] ScriptedHashUpsertStart = "{ \"scripted_upsert\": true, \"upsert\": {}, \"script\": { \"source\": \"if (ctx._source."u8.ToArray();
	private static readonly byte[] ScriptedHashUpsertAfterIfCheck = " == params.hash ) { "u8.ToArray();
	private static readonly byte[] ScriptedHashUpdateScript = "ctx.op = 'noop'"u8.ToArray();
	private static readonly byte[] ScriptedHashParamComma = ", "u8.ToArray();
	private static readonly byte[] ScriptedHashKeySeparator = ": "u8.ToArray();
	private static readonly byte[] ScriptedHashElseBranchStart = " } else { ctx._source = params.doc; ctx._source."u8.ToArray();
	private static readonly byte[] ScriptedHashElseBranchEnd = " = params.hash } \", \"params\": { \"hash\": "u8.ToArray();
	private static readonly byte[] ScriptHashDocAsParameter = ", \"doc\":"u8.ToArray();
	private static readonly byte[] ScriptedHashUpsertEnd = " } } }"u8.ToArray();

	/// <summary>The serializer options channels use: relaxed escaping and <see cref="JsonIgnoreCondition.WhenWritingDefault"/>.</summary>
	public static JsonSerializerOptions ChannelLikeOptions(JsonSerializerContext context) => new()
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
		TypeInfoResolver = JsonTypeInfoResolver.Combine(new DefaultJsonTypeInfoResolver(), context)
	};

	public static BulkOperationHeader ToLegacy(BulkAction a)
	{
		var alias = a.RequireAlias ? true : (bool?)null;
		var templates = a.DynamicTemplates?.ToDictionary(k => k.Key, k => k.Value);
		return a.Kind switch
		{
			BulkActionKind.Index => new IndexOperation { Index = a.IndexName, Id = a.Id, RequireAlias = alias, DynamicTemplates = templates },
			BulkActionKind.Create => new CreateOperation { Index = a.IndexName, Id = a.Id, RequireAlias = alias, DynamicTemplates = templates },
			BulkActionKind.Update => new UpdateOperation { Index = a.IndexName, Id = a.Id, RequireAlias = alias },
			BulkActionKind.ScriptedHashUpsert => new ScriptedHashUpdateOperation { Index = a.IndexName, Id = a.Id, RequireAlias = alias, UpdateInformation = a.HashUpdate! },
			_ => throw new NotSupportedException("the legacy writer has no delete")
		};
	}

	public static byte[] GetBytes<TEvent>(TEvent[] page, JsonSerializerOptions options, Func<int, BulkOperationHeader> headerFor)
	{
		var writerOptions = new JsonWriterOptions { Encoder = options.Encoder, Indented = options.WriteIndented, SkipValidation = true };
		var bufferWriter = new ArrayBufferWriter<byte>();
		using var writer = new Utf8JsonWriter(bufferWriter, writerOptions);
		for (var i = 0; i < page.Length; i++)
		{
			var @event = page[i];
			var header = headerFor(i);
			JsonSerializer.Serialize(writer, header, header.GetType(), options);
			bufferWriter.Write(LineFeed);
			writer.Reset();

			if (header is UpdateOperation)
			{
				bufferWriter.Write(DocUpdateHeaderStart);
				writer.Reset();
			}
			if (header is ScriptedHashUpdateOperation hashUpdate)
			{
				bufferWriter.Write(ScriptedHashUpsertStart);
				writer.Reset();
				var field = Encoding.UTF8.GetBytes(hashUpdate.UpdateInformation.Field);
				bufferWriter.Write(field);
				writer.Reset();
				bufferWriter.Write(ScriptedHashUpsertAfterIfCheck);
				writer.Reset();
				if (hashUpdate.UpdateInformation.UpdateScript is not null)
					bufferWriter.Write(Encoding.UTF8.GetBytes(hashUpdate.UpdateInformation.UpdateScript));
				else
					bufferWriter.Write(ScriptedHashUpdateScript);
				writer.Reset();
				bufferWriter.Write(ScriptedHashElseBranchStart);
				writer.Reset();
				bufferWriter.Write(field);
				writer.Reset();
				bufferWriter.Write(ScriptedHashElseBranchEnd);
				writer.Reset();
				JsonSerializer.Serialize(writer, hashUpdate.UpdateInformation.Hash, options);

				if (hashUpdate.UpdateInformation.Parameters is not null)
					foreach (var (key, value) in hashUpdate.UpdateInformation.Parameters)
					{
						bufferWriter.Write(ScriptedHashParamComma);
						writer.Reset();
						JsonSerializer.Serialize(writer, key, options);
						bufferWriter.Write(ScriptedHashKeySeparator);
						writer.Reset();
						JsonSerializer.Serialize(writer, value, options);
					}

				bufferWriter.Write(ScriptHashDocAsParameter);
				writer.Reset();
			}

			JsonSerializer.Serialize(writer, @event, options);
			writer.Reset();

			if (header is UpdateOperation)
			{
				bufferWriter.Write(DocUpdateHeaderEnd);
				writer.Reset();
			}
			if (header is ScriptedHashUpdateOperation)
			{
				bufferWriter.Write(ScriptedHashUpsertEnd);
				writer.Reset();
			}
			bufferWriter.Write(LineFeed);
			writer.Reset();
		}
		return bufferWriter.WrittenSpan.ToArray();
	}
}
