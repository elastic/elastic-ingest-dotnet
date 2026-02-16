// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

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
}
