// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

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

	/// <summary>Documents that failed enrichment (LLM error, parse failure).</summary>
	public int Failed { get; set; }

	/// <summary>Whether the run hit <see cref="AiEnrichmentOptions.MaxEnrichmentsPerRun"/>.</summary>
	public bool ReachedLimit { get; set; }
}
