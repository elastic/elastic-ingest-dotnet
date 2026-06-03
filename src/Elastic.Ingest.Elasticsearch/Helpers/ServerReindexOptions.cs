// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using Elastic.Mapping;

namespace Elastic.Ingest.Elasticsearch.Helpers;

/// <summary>
/// Configuration for <see cref="ServerReindex"/>.
/// </summary>
public class ServerReindexOptions
{
	/// <summary> Source index name. When null, resolved from <see cref="SourceContext"/> (WriteAlias). </summary>
	public string? Source { get; init; }

	/// <summary> Destination index name. When null, resolved from <see cref="DestinationContext"/> (WriteAlias). </summary>
	public string? Destination { get; init; }

	/// <summary> Optional type context for automatic source index resolution. Uses WriteAlias. </summary>
	public ElasticsearchTypeContext? SourceContext { get; init; }

	/// <summary> Optional type context for automatic destination index resolution. Uses WriteAlias. </summary>
	public ElasticsearchTypeContext? DestinationContext { get; init; }

	/// <summary> Optional JSON query body to filter source documents. </summary>
	public string? Query { get; init; }

	/// <summary> Optional ingest pipeline name. </summary>
	public string? Pipeline { get; init; }

	/// <summary> Throttle in requests per second. -1 for unlimited. </summary>
	public float? RequestsPerSecond { get; init; }

	/// <summary> Number of slices: "auto" or a number string. Slicing is not supported for remote reindex. </summary>
	public string? Slices { get; init; }

	/// <summary>
	/// Number of documents to read per batch from the source. Defaults to 1000.
	/// Lower this for remote reindex with very large documents.
	/// Maps to <c>source.size</c> in the request body.
	/// </summary>
	public int? SourceSize { get; init; }

	/// <summary>
	/// Maximum number of documents to reindex. When set, the operation stops after this many
	/// documents have been processed. Maps to the top-level <c>max_docs</c> field.
	/// </summary>
	public long? MaxDocs { get; init; }

	/// <summary>
	/// How to handle version conflicts: <c>"abort"</c> (default) or <c>"proceed"</c>.
	/// Set to <c>"proceed"</c> to continue reindexing when conflicts occur (useful for retries).
	/// </summary>
	public string? Conflicts { get; init; }

	/// <summary>
	/// Optional Painless script to modify documents during reindex. Maps to the top-level
	/// <c>script</c> object in the request body. Provide the full JSON object, e.g.
	/// <c>{"lang":"painless","source":"ctx._source.tag = 'migrated'"}</c>.
	/// </summary>
	public string? Script { get; init; }

	/// <summary>
	/// When <c>true</c>, strips the <c>_inference_fields</c> metadata from each document's
	/// <c>_source</c> before it reaches the destination. This works around a known Elasticsearch
	/// issue where reindex-from-remote fails with "Duplicate field '_inference_fields'" on indices
	/// containing <c>semantic_text</c> fields.
	/// <para>
	/// Implemented via <c>_source</c> exclusion, so it composes cleanly with <see cref="Script"/>.
	/// </para>
	/// <para>
	/// <b>Caveat:</b> removing <c>_inference_fields</c> causes the destination cluster to re-run
	/// inference even when chunk embeddings are already present in <c>_source</c>. This is an
	/// Elasticsearch-side limitation — see
	/// <see href="https://github.com/elastic/elasticsearch/issues/150634">#150634</see>.
	/// </para>
	/// </summary>
	public bool ExcludeInferenceFields { get; init; }

	/// <summary> How often to poll the task status. Defaults to 5 seconds. </summary>
	public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

	/// <summary>
	/// Remote Elasticsearch cluster to read source documents from.
	/// When set, the <c>source.remote</c> block is included in the <c>_reindex</c> request body.
	/// <see cref="Source"/> is still required — it specifies the index on the remote cluster.
	/// </summary>
	public RemoteSource? Remote { get; init; }

	/// <summary> Optional full override body JSON. When set, Source/Destination/Query/Pipeline/Remote and other structured options are ignored. </summary>
	public string? Body { get; init; }
}
