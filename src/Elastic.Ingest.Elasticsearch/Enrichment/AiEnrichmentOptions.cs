// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elastic.Ingest.Elasticsearch.Enrichment;

/// <summary>
/// Per-call configuration for <see cref="AiEnrichmentOrchestrator.EnrichAsync"/>.
/// </summary>
public sealed class AiEnrichmentOptions
{
	/// <summary>
	/// Hard cap on enrichments per run. Deploys run multiple times a day;
	/// coverage grows incrementally across runs. Default: 100.
	/// </summary>
	public int MaxEnrichmentsPerRun { get; set; } = 100;

	/// <summary>
	/// Documents to fetch per <c>search_after</c> page when querying for candidates. Default: 50.
	/// </summary>
	public int QueryBatchSize { get; set; } = 50;

	/// <summary>
	/// Elasticsearch inference endpoint ID for ES|QL COMPLETION calls.
	/// Default: <c>.gp-llm-v2-completion</c>.
	/// </summary>
	public string InferenceEndpointId { get; set; } = ".gp-llm-v2-completion";

	/// <summary>
	/// Documents per ES|QL COMPLETION query. Each query processes rows
	/// sequentially on the server, so keep this small and rely on
	/// <see cref="EsqlConcurrency"/> for parallelism. Default: 20.
	/// </summary>
	public int EsqlBatchSize { get; set; } = 20;

	/// <summary>
	/// Maximum concurrent ES|QL COMPLETION queries. Default: 8.
	/// Combined with <see cref="EsqlBatchSize"/>=20, processes up to 160 docs in flight.
	/// </summary>
	public int EsqlConcurrency { get; set; } = 8;

	/// <summary>
	/// Per-request timeout for each ES|QL COMPLETION call. Default: 5 minutes.
	/// LLM-backed completion can be slow for large batches; set this high enough
	/// to avoid premature 408 responses from Elasticsearch.
	/// </summary>
	public TimeSpan CompletionTimeout { get; set; } = TimeSpan.FromMinutes(5);

	/// <summary>
	/// Maximum number of retries for a failed ES|QL COMPLETION call (e.g. HTTP 408/429/5xx).
	/// Default: 2. Each retry halves the batch size (down to <see cref="MinCompletionBatchSize"/>)
	/// and fans out the sub-chunks concurrently. The delay between retries equals
	/// <see cref="CompletionTimeout"/> capped at 1 minute.
	/// </summary>
	public int CompletionMaxRetries { get; set; } = 2;

	/// <summary>
	/// Floor for batch size reduction during timeout retries. When a COMPLETION query
	/// times out, the batch is halved and retried; this sets the minimum chunk size.
	/// Must be at least 1. Default: 5.
	/// </summary>
	public int MinCompletionBatchSize { get; set; } = 5;
}

/// <summary>
/// Progress snapshot yielded during <see cref="AiEnrichmentOrchestrator.EnrichAsync"/>.
/// Iterate with <c>await foreach</c>. The final item always has <see cref="Phase"/> = <see cref="AiEnrichmentPhase.Complete"/>.
/// </summary>
public sealed class AiEnrichmentProgress
{
	/// <summary>The current phase of the enrichment run.</summary>
	public required AiEnrichmentPhase Phase { get; init; }

	/// <summary>Running total of documents enriched so far.</summary>
	public int Enriched { get; init; }

	/// <summary>Running total of documents failed so far.</summary>
	public int Failed { get; init; }

	/// <summary>Running total of candidates discovered so far.</summary>
	public int TotalCandidates { get; init; }

	/// <summary>Optional message with phase-specific detail.</summary>
	public string? Message { get; init; }
}

/// <summary>
/// Phases reported by <see cref="AiEnrichmentProgress"/>.
/// </summary>
public enum AiEnrichmentPhase
{
	/// <summary>Querying for candidate documents.</summary>
	Querying,
	/// <summary>An ES|QL COMPLETION chunk completed (yielded per-chunk).</summary>
	Enriching,
	/// <summary>Refreshing the lookup index.</summary>
	Refreshing,
	/// <summary>Executing the enrich policy.</summary>
	ExecutingPolicy,
	/// <summary>Backfilling enriched data into the target index.</summary>
	Backfilling,
	/// <summary>Enrichment run completed.</summary>
	Complete
}

/// <summary>
/// A pending update for the lookup index. <see cref="UrlHash"/> is used as the <c>_id</c>
/// in the bulk update header. <see cref="Document"/> is serialized as the <c>doc</c> body.
/// </summary>
internal sealed record LookupUpdate(string UrlHash, JsonElement Document);

/// <summary>
/// Result of a single ES|QL COMPLETION chunk.
/// </summary>
internal sealed record EsqlChunkResult(List<LookupUpdate> Updates, int Failed, string? Error, bool IsRetryable = false);

/// <summary>
/// Tracks a chunk of doc IDs and its current retry depth for binary-split retries.
/// </summary>
internal sealed record ChunkMeta(List<string> DocIds, int Depth);

/// <summary>
/// Wraps a query body for <c>_search</c>, <c>_update_by_query</c>, etc.
/// </summary>
internal sealed class QueryRequest
{
	[JsonPropertyName("query")]
	public JsonElement Query { get; init; }
}
