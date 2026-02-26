// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elastic.Ingest.Elasticsearch.Enrichment;

/// <summary>
/// Configuration for post-indexing AI enrichment.
/// </summary>
public sealed class AiEnrichmentOptions
{
	/// <summary>
	/// Hard cap on enrichments per run. Deploys run multiple times a day;
	/// coverage grows incrementally across runs. Default: 100.
	/// </summary>
	public int MaxEnrichmentsPerRun { get; set; } = 100;

	/// <summary>
	/// Maximum concurrent LLM inference calls. Default: 4.
	/// </summary>
	public int MaxConcurrency { get; set; } = 4;

	/// <summary>
	/// Documents to fetch per <c>search_after</c> page when querying for candidates. Default: 50.
	/// </summary>
	public int QueryBatchSize { get; set; } = 50;

	/// <summary>
	/// Elasticsearch inference endpoint ID for completion calls.
	/// Default: <c>.gp-llm-v2-completion</c>.
	/// </summary>
	public string InferenceEndpointId { get; set; } = ".gp-llm-v2-completion";
}

/// <summary>
/// Result of an AI enrichment run.
/// </summary>
public sealed class AiEnrichmentResult
{
	/// <summary>Total documents identified as needing enrichment.</summary>
	public int TotalCandidates { get; set; }

	/// <summary>Documents successfully enriched in this run.</summary>
	public int Enriched { get; set; }

	/// <summary>Documents skipped (e.g. empty input fields).</summary>
	public int Skipped { get; set; }

	/// <summary>Documents that failed enrichment (LLM error, parse failure).</summary>
	public int Failed { get; set; }

	/// <summary>Whether the run hit <see cref="AiEnrichmentOptions.MaxEnrichmentsPerRun"/>.</summary>
	public bool ReachedLimit { get; set; }
}

/// <summary>
/// Outcome of enriching a single candidate document.
/// </summary>
internal sealed record EnrichmentOutcome(string Id, EnrichmentStatus Status, LookupUpdate? Update = null);

/// <summary>
/// Distinguishes successful enrichment from legitimate skips and actual failures.
/// </summary>
internal enum EnrichmentStatus
{
	/// <summary>LLM returned valid enrichment data.</summary>
	Enriched,
	/// <summary>Document was skipped (empty input, no stale fields, null prompt).</summary>
	Skipped,
	/// <summary>Enrichment failed (LLM error, parse failure, exception).</summary>
	Failed
}

/// <summary>
/// A pending update for the lookup index. <see cref="UrlHash"/> is used as the <c>_id</c>
/// in the bulk update header. <see cref="Document"/> is serialized as the <c>doc</c> body.
/// </summary>
internal sealed record LookupUpdate(string UrlHash, JsonElement Document);

/// <summary>
/// Result of querying for candidate documents needing enrichment.
/// </summary>
internal sealed record CandidateQueryResult(List<CandidateDocument> Candidates, object[]? SearchAfter);

/// <summary>
/// Result of processing a batch of candidate documents.
/// </summary>
internal sealed record BatchResult(int Enriched, int Skipped, int Failed);

/// <summary>
/// A candidate document identified as needing enrichment.
/// </summary>
/// <summary>
/// A candidate document identified as needing enrichment.
/// </summary>
internal sealed record CandidateDocument(string Id, JsonElement Source);

/// <summary>
/// Request body for the <c>_inference/completion</c> API.
/// </summary>
internal sealed class CompletionRequest
{
	[JsonPropertyName("input")]
	public string Input { get; init; } = null!;
}

/// <summary>
/// Wraps a query body for <c>_search</c>, <c>_update_by_query</c>, etc.
/// </summary>
internal sealed class QueryRequest
{
	[JsonPropertyName("query")]
	public JsonElement Query { get; init; }
}
