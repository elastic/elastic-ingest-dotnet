// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elastic.Ingest.Elasticsearch;

internal static class ElasticsearchChannelStatics
{
	public static readonly byte[] LineFeed = { (byte)'\n' };

	public static readonly byte[] DocUpdateHeaderStart = "{\"doc_as_upsert\": true, \"doc\": "u8.ToArray();
	public static readonly byte[] DocUpdateHeaderEnd = " }"u8.ToArray();

	public static readonly byte[] ScriptedHashUpsertStart =
		"{ \"scripted_upsert\": true, \"upsert\": {}, \"script\": { \"source\": \"if (ctx._source."u8.ToArray();

	public static readonly byte[] ScriptedHashUpsertAfterIfCheck = " == params.hash ) { "u8.ToArray();

	public static readonly byte[] ScriptedHashUpdateScript = "ctx.op = 'noop'"u8.ToArray();

	public static readonly byte[] ScriptedHashParamComma = ", "u8.ToArray();
	public static readonly byte[] ScriptedHashKeySeparator = ": "u8.ToArray();

	public static readonly byte[] ScriptedHashAfterIfCheckOp =
		" } else { ctx._source = params.doc } \", \"params\": { \"hash\": "u8.ToArray();

	public static readonly byte[] ScriptHashDocAsParameter =
		", \"doc\":"u8.ToArray();

	public static readonly byte[] ScriptedHashUpsertEnd = " } } }"u8.ToArray();

	public static readonly HashSet<int> RetryStatusCodes = [502, 503, 504, 429];

	public static readonly JsonSerializerOptions SerializerOptions = new ()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	public static readonly JsonWriterOptions WriterOptions =
		// SkipValidation as we write ndjson
		new() { Encoder = SerializerOptions.Encoder, Indented = SerializerOptions.WriteIndented, SkipValidation = true};
}
