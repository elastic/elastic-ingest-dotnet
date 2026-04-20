// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using Elastic.Mapping;

namespace Elastic.Ingest.Elasticsearch.Helpers;

/// <summary>
/// Configuration for <see cref="PointInTimeSearch{TDocument}"/>
/// </summary>
public class PointInTimeSearchOptions
{
	/// <summary> The index to search. When null, resolved from <see cref="TypeContext"/> (ReadAlias ?? WriteAlias). </summary>
	public string? Index { get; init; }

	/// <summary> Optional type context for automatic index resolution. Uses ReadAlias if available, otherwise WriteAlias. </summary>
	public ElasticsearchTypeContext? TypeContext { get; init; }

	/// <summary> JSON query clause, e.g. {"match_all":{}}. If null, matches all documents. </summary>
	public string? QueryBody { get; init; }

	/// <summary> JSON sort array. Defaults to ["_shard_doc"] for optimal PIT performance. </summary>
	public string? Sort { get; init; }

	/// <summary> Number of documents per page. Defaults to 1000. </summary>
	public int Size { get; init; } = 1000;

	/// <summary> How long the PIT should be kept alive between requests. Defaults to "5m". </summary>
	public string KeepAlive { get; init; } = "5m";

	/// <summary>
	/// Number of slices for parallel searching. null = auto-detect (based on shard count),
	/// 0 or 1 = no slicing.
	/// </summary>
	public int? Slices { get; init; }

	/// <summary>
	/// Field names to include in each hit’s <c>_source</c>, matching Elasticsearch’s
	/// <c>_source.includes</c> filtering. When null or empty and <see cref="SourceExcludes"/> is also
	/// unset or empty, no <c>_source</c> clause is sent and the full stored source is returned.
	/// </summary>
	/// <remarks>
	/// Responses may contain only a subset of JSON properties; <see cref="PointInTimeSearch{TDocument}"/>
	/// still deserializes each hit’s <c>_source</c> into the document type using
	/// <see cref="System.Text.Json.JsonSerializer"/>, so members missing from the JSON remain at their
	/// default values. Use a narrower document type or <see cref="System.Text.Json.JsonElement"/> when
	/// you only map a few fields.
	/// </remarks>
	public IReadOnlyList<string>? SourceIncludes { get; init; }

	/// <summary>
	/// Field names to exclude from each hit’s <c>_source</c>, matching Elasticsearch’s
	/// <c>_source.excludes</c> filtering. When null or empty and <see cref="SourceIncludes"/> is also
	/// unset or empty, no <c>_source</c> clause is sent.
	/// </summary>
	/// <remarks>
	/// See <see cref="SourceIncludes"/> for how partial <c>_source</c> JSON interacts with deserialization.
	/// </remarks>
	public IReadOnlyList<string>? SourceExcludes { get; init; }
}
