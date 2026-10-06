// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Ingest.Elasticsearch.Bulk;
using Elastic.Ingest.Elasticsearch.Serialization;

namespace Elastic.Ingest.Elasticsearch;

internal static class IngestChannelStatics
{
	public static readonly byte[] LineFeed = [(byte)'\n'];

	public static readonly byte[] DocUpdateHeaderStart = "{\"doc_as_upsert\": true, \"doc\": "u8.ToArray();
	public static readonly byte[] DocUpdateHeaderEnd = " }"u8.ToArray();

	public static readonly byte[] ScriptedHashUpsertStart =
		"{ \"scripted_upsert\": true, \"upsert\": {}, \"script\": { \"source\": \"if (ctx._source."u8.ToArray();

	public static readonly byte[] ScriptedHashUpsertAfterIfCheck = " == params.hash ) { "u8.ToArray();

	public static readonly byte[] ScriptedHashUpdateScript = "ctx.op = 'noop'"u8.ToArray();

	public static readonly byte[] ScriptedHashParamComma = ", "u8.ToArray();
	public static readonly byte[] ScriptedHashKeySeparator = ": "u8.ToArray();

	public static readonly byte[] ScriptedHashElseBranchStart =
		" } else { ctx._source = params.doc; ctx._source."u8.ToArray();

	public static readonly byte[] ScriptedHashElseBranchEnd =
		" = params.hash } \", \"params\": { \"hash\": "u8.ToArray();

	public static readonly byte[] ScriptHashDocAsParameter =
		", \"doc\":"u8.ToArray();

	public static readonly byte[] ScriptedHashUpsertEnd = " } } }"u8.ToArray();

	public const string DefaultBulkPathAndQuery = "_bulk?filter_path=errors,error,items.*.status,items.*.error,items.*.result,items.*._version";

	private const string DefaultBulkFilterPath = "filter_path=errors,error,items.*.status,items.*.error,items.*.result,items.*._version";

	/// <summary>
	/// Adds <c>items.*._id</c> and/or <c>items.*._index</c> to the <c>filter_path</c> of a bulk url built from
	/// <see cref="DefaultBulkPathAndQuery"/>, so the response reports them. Fields that are already requested are not added twice.
	/// </summary>
	public static string WithItemIdentity(string bulkPathAndQuery, Track track)
	{
		var extra = string.Empty;
		if ((track & Track.Id) != 0 && !bulkPathAndQuery.Contains("items.*._id", System.StringComparison.Ordinal)) extra += ",items.*._id";
		if ((track & Track.Index) != 0 && !bulkPathAndQuery.Contains("items.*._index", System.StringComparison.Ordinal)) extra += ",items.*._index";
		return extra.Length == 0 ? bulkPathAndQuery : bulkPathAndQuery.Replace(DefaultBulkFilterPath, DefaultBulkFilterPath + extra);
	}

	public static readonly HashSet<int> RetryStatusCodes = [502, 503, 504, 429];

	public static readonly JsonSerializerOptions SerializerOptions = new(IngestSerializationContext.Default.Options)
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	public static readonly JsonWriterOptions WriterOptions =
		// SkipValidation as we write ndjson
		new() { Encoder = SerializerOptions.Encoder, Indented = SerializerOptions.WriteIndented, SkipValidation = true};
}
