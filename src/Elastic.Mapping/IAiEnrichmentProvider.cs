// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Text.Json;

namespace Elastic.Mapping;

/// <summary>
/// Provides AI enrichment capabilities for indexed documents.
/// Implementations are typically source-generated from <c>[AiEnrichment&lt;T&gt;]</c>
/// combined with <c>[AiInput]</c> and <c>[AiField]</c> attribute declarations.
/// <para>
/// Supports per-field prompt hashing: changing one field's prompt only invalidates
/// that field's cached enrichment, not all fields.
/// </para>
/// </summary>
public interface IAiEnrichmentProvider
{
	// ── Per-field metadata ──

	/// <summary>
	/// Maps each AI output field name to the SHA-256 hash of its prompt description.
	/// Used for per-field staleness detection.
	/// </summary>
	IReadOnlyDictionary<string, string> FieldPromptHashes { get; }

	/// <summary>
	/// Maps each AI output field name to its prompt hash field name in the index.
	/// e.g. <c>"ai_questions"</c> → <c>"ai_questions_ph"</c>.
	/// </summary>
	IReadOnlyDictionary<string, string> FieldPromptHashFieldNames { get; }

	/// <summary>
	/// AI output field names whose absence marks a document as needing enrichment.
	/// </summary>
	string[] EnrichmentFields { get; }

	/// <summary>
	/// Source fields to retrieve from the index for prompt building.
	/// </summary>
	string[] RequiredSourceFields { get; }

	// ── Prompt &amp; parsing (per-field granularity) ──

	/// <summary>
	/// Builds the LLM prompt from a document's source fields, targeting only the specified
	/// stale fields. The JSON schema in the prompt only includes those fields.
	/// Returns <c>null</c> to skip this document.
	/// </summary>
	string? BuildPrompt(JsonElement source, IReadOnlyCollection<string> staleFields);

	/// <summary>
	/// Parses the raw LLM response and serializes it as a partial-document JSON string
	/// for a lookup index <c>_update</c>. Only includes the enriched fields and their
	/// per-field prompt hashes.
	/// Returns <c>null</c> if parsing fails.
	/// </summary>
	string? ParseResponse(string llmResponse, IReadOnlyCollection<string> enrichedFields);

	// ── Lookup infrastructure (generated from [AiField] declarations) ──

	/// <summary>The name of the lookup index that stores enrichment data.</summary>
	string LookupIndexName { get; }

	/// <summary>The mapping JSON for the lookup index.</summary>
	string LookupIndexMapping { get; }

	/// <summary>The document field used as the match key between lookup and documents (e.g. <c>"url"</c>).</summary>
	string MatchField { get; }

	/// <summary>The name of the Elasticsearch enrich policy (versioned by fields hash).</summary>
	string EnrichPolicyName { get; }

	/// <summary>The JSON body for creating the enrich policy.</summary>
	string EnrichPolicyBody { get; }

	/// <summary>The name of the ingest pipeline.</summary>
	string PipelineName { get; }

	/// <summary>The JSON body for creating the ingest pipeline.</summary>
	string PipelineBody { get; }
}
