# AI Enrichment Plan

## Overview

Extract a reusable AI enrichment system from `docs-builder` into `elastic-ingest-dotnet`.
The system enriches indexed documents with LLM-generated metadata (summaries, questions, use cases)
using a **lookup index + enrich policy** pattern that survives index rollovers, with **per-field
prompt hashing** so that changing one prompt or adding a new field doesn't invalidate all cached enrichments.

## Why a lookup index is necessary

The `IncrementalSyncOrchestrator` manages two indices (primary lexical + secondary semantic).
The secondary can **roll over** when mappings change (new field, analyzer change, etc.),
creating fresh backing indices. When this happens:

- All documents are re-indexed from scratch (Multiplex strategy)
- The old backing index with AI enrichments is gone
- Without a persistent store, every LLM call must be repeated

The **lookup index** is a separate index that stores enrichment data keyed by URL.
It survives rollovers. An **enrich policy + ingest pipeline** re-applies cached
enrichments to documents as they flow into the new secondary index — no LLM calls needed.

## What changes from the current docs-builder approach

### Eliminated

| Component | Why it existed | Why it's gone |
|---|---|---|
| `ElasticsearchEnrichmentCache` | In-memory mirror of lookup index via scroll at startup | Post-indexing model queries ES directly; no in-memory state |
| `TryEnrichDocumentAsync` | Per-document inline enrichment during export | Export loop no longer touches enrichment |
| `EnrichmentKeyGenerator` | Content hash for cache keying | URL used as lookup key; content changes detected via prompt hash on secondary |
| `_enrichmentCount`, `_cacheHitCount` fields on exporter | Inline tracking during export | Orchestrator tracks its own state |
| Single combined `PromptHash` | One hash for all prompts | Per-field prompt hashes for granular invalidation |
| Hand-written lookup index mapping, enrich policy, pipeline JSON | No generation | Source-generated from `[AiField]` declarations |

### Kept (structurally correct)

| Component | Why |
|---|---|
| Lookup index | Survives rollovers; avoids re-calling LLM |
| Enrich policy + ingest pipeline | Joins lookup → documents at index time |
| `default_pipeline` on secondary | Applies enrichments during both direct writes and reindex |
| `MaxEnrichmentsPerRun` hard cap | Keeps deploys bounded; coverage grows incrementally |

## Per-field prompt hashing

### Problem with single hash

```
PromptHash = SHA256(all field descriptions concatenated)
```

Changing one field's description or adding a new field invalidates the hash for ALL fields.
Every document needs ALL fields regenerated.

### Solution: per-field prompt hashes

Each `[AiField]` gets its own prompt hash = `SHA256(that field's description)`.
The lookup index stores both the value and the hash for each field:

```
Lookup entry for url="/docs/search/api":
  ai_questions: [...]                    ai_questions_ph: "abc1"
  ai_rag_optimized_summary: "..."        ai_rag_optimized_summary_ph: "def2"
  ai_short_summary: "..."               ai_short_summary_ph: "ghi3"
```

The enrich pipeline copies both value and hash fields to the document on the secondary.

### Granular invalidation scenarios

**Change one field's prompt:**
1. Only that field's `_ph` changes
2. Post-indexing query finds docs where that field's hash is stale
3. LLM prompt only asks for that one field (smaller, cheaper)
4. Partial update on lookup entry preserves all other fields

**Add a new field:**
1. New field doesn't exist in lookup or on documents
2. Post-indexing query finds docs where the new field is NULL
3. LLM prompt only asks for the new field
4. Partial update adds just that field to existing lookup entries

**No changes:**
- All per-field hashes match → 0 LLM calls

## Architecture

```
                    ┌─────────────────────────┐
                    │   Lookup Index           │
                    │   (survives rollovers)   │
                    │                          │
                    │ url, ai_questions,       │
                    │ ai_questions_ph,         │
                    │ ai_summary,              │
                    │ ai_summary_ph, ...       │
                    └────────┬────────────────┘
                             │
                    ┌────────▼────────────────┐
                    │   Enrich Policy          │
                    │   (rebuilt after new     │
                    │    entries added)         │
                    └────────┬────────────────┘
                             │
                    ┌────────▼────────────────┐
                    │   Ingest Pipeline        │
                    │   (default_pipeline on   │
                    │    secondary index)       │
                    └────────┬────────────────┘
                             │
    ┌──────────────┐   ┌─────▼──────────────┐
    │   Primary    │──▶│   Secondary         │
    │   (lexical)  │   │   (semantic + AI)   │
    │              │   │                     │
    │   No AI      │   │   AI fields applied │
    │   fields     │   │   by pipeline from  │
    │              │   │   lookup index       │
    └──────────────┘   └─────────────────────┘
                             │
                    ┌────────▼────────────────┐
                    │   Post-indexing          │
                    │   AiEnrichmentOrchestrator│
                    │                          │
                    │  1. Query secondary for  │
                    │     stale/missing fields │
                    │  2. Per-field: only call │
                    │     LLM for stale ones   │
                    │  3. Partial-update lookup│
                    │  4. Re-execute policy    │
                    │  5. Backfill secondary   │
                    └──────────────────────────┘
```

## Components

### 1. Attributes — `Elastic.Mapping`

New files in `Elastic.Mapping/Attributes/`:

```csharp
// AiEnrichmentAttribute.cs
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class AiEnrichmentAttribute<TDocument> : Attribute
    where TDocument : class
{
    /// Optional role preamble for the generated prompt.
    public string? Role { get; init; }

    /// Lookup index name. Defaults to "{index-name}-ai-enrichment-cache".
    public string? LookupIndexName { get; init; }
}
```

```csharp
// AiInputAttribute.cs
[AttributeUsage(AttributeTargets.Property)]
public sealed class AiInputAttribute : Attribute;
```

```csharp
// AiFieldAttribute.cs
[AttributeUsage(AttributeTargets.Property)]
public sealed class AiFieldAttribute : Attribute
{
    public AiFieldAttribute(string description) => Description = description;

    /// Description included in the JSON schema sent to the LLM.
    public string Description { get; }

    /// Min items for string[] fields.
    public int MinItems { get; init; }

    /// Max items for string[] fields.
    public int MaxItems { get; init; }
}
```

### 2. Consumer declaration

The document type uses `[AiInput]` and `[AiField]` alongside existing mapping attributes:

```csharp
public class DocumentationDocument
{
    [Text, AiInput, JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [Text, AiInput, JsonPropertyName("stripped_body")]
    public string? StrippedBody { get; set; }

    [Text, AiField("3-5 sentences with technical entities for RAG retrieval.")]
    [JsonPropertyName("ai_rag_optimized_summary")]
    public string? AiRagOptimizedSummary { get; set; }

    [Text, AiField("Exactly 5-10 words, action-oriented, starts with a verb.")]
    [JsonPropertyName("ai_short_summary")]
    public string? AiShortSummary { get; set; }

    [Text, AiField("3-8 keywords representing a realistic developer search query.")]
    [JsonPropertyName("ai_search_query")]
    public string? AiSearchQuery { get; set; }

    [Text, AiField("Natural questions a developer would ask (6-15 words each).",
        MinItems = 3, MaxItems = 5)]
    [JsonPropertyName("ai_questions")]
    public string[]? AiQuestions { get; set; }

    [Text, AiField("Simple 2-4 word tasks a developer wants to accomplish.",
        MinItems = 2, MaxItems = 4)]
    [JsonPropertyName("ai_use_cases")]
    public string[]? AiUseCases { get; set; }

    [Keyword, JsonPropertyName("ai_questions_ph")]
    public string? AiQuestionsPh { get; set; }
    // ... other per-field prompt hash properties (or omitted if pipeline sets them)
}
```

Mapping context — one attribute triggers generation:

```csharp
[ElasticsearchMappingContext]
[Index<DocumentationDocument>(Name = "docs", WriteAlias = "docs-content", ...)]
[AiEnrichment<DocumentationDocument>(
    Role = "Expert technical writer creating search metadata for Elastic documentation."
)]
static partial class DocumentationMappingContext;
```

### 3. Interface — `Elastic.Ingest.Elasticsearch/Enrichment/IAiEnrichmentProvider.cs`

```csharp
public interface IAiEnrichmentProvider
{
    // ── Per-field metadata ──
    IReadOnlyDictionary<string, string> FieldPromptHashes { get; }
    IReadOnlyDictionary<string, string> FieldPromptHashFieldNames { get; }
    string[] EnrichmentFields { get; }
    string[] RequiredSourceFields { get; }

    // ── Prompt & parsing (per-field granularity) ──
    string? BuildPrompt(JsonElement source, IReadOnlyCollection<string> staleFields);
    string? ParseResponse(string llmResponse, IReadOnlyCollection<string> enrichedFields);

    // ── Lookup infrastructure (generated from [AiField] declarations) ──
    string LookupIndexName { get; }
    string LookupIndexMapping { get; }
    string MatchField { get; }
    string EnrichPolicyName { get; }
    string EnrichPolicyBody { get; }
    string PipelineName { get; }
    string PipelineBody { get; }
}
```

### 4. Options — `Elastic.Ingest.Elasticsearch/Enrichment/AiEnrichmentOptions.cs`

```csharp
public sealed record AiEnrichmentOptions
{
    /// Hard cap on enrichments per run. Default: 100.
    public int MaxEnrichmentsPerRun { get; init; } = 100;

    /// Max concurrent LLM calls. Default: 4.
    public int MaxConcurrency { get; init; } = 4;

    /// Documents per search_after page. Default: 50.
    public int QueryBatchSize { get; init; } = 50;

    /// Inference endpoint ID. Default: ".gp-llm-v2-completion".
    public string InferenceEndpointId { get; init; } = ".gp-llm-v2-completion";
}

public sealed record AiEnrichmentResult
{
    public int TotalCandidates { get; init; }
    public int Enriched { get; init; }
    public int Failed { get; init; }
    public bool ReachedLimit { get; init; }
}
```

### 5. Orchestrator — `Elastic.Ingest.Elasticsearch/Enrichment/AiEnrichmentOrchestrator.cs`

```csharp
public class AiEnrichmentOrchestrator
{
    public AiEnrichmentOrchestrator(
        ITransport transport,
        IAiEnrichmentProvider provider,
        AiEnrichmentOptions? options = null);

    // ── Pre-bootstrap (before indexing starts) ──
    // 1. Ensure lookup index exists
    // 2. Ensure enrich policy exists (versioned by field hash)
    // 3. Execute enrich policy
    // 4. Ensure ingest pipeline exists
    public async Task InitializeAsync(CancellationToken ct);

    // ── Post-indexing enrichment ──
    // 1. Query targetIndex for candidates (any field missing or stale)
    //    _source includes input fields + per-field prompt hashes
    // 2. For each candidate (WhenEach on .NET 9+ / WhenAny+HashSet fallback):
    //    a. Compare per-field prompt hashes → determine staleFields
    //    b. BuildPrompt(source, staleFields)
    //    c. Call _inference/completion endpoint
    //    d. ParseResponse(response, staleFields) → partial lookup JSON
    //    e. POST lookup/_update/{url_hash} {"doc": <partial>, "doc_as_upsert": true}
    // 3. Re-execute enrich policy
    // 4. _update_by_query on targetIndex with pipeline for stale/missing docs
    public async Task<AiEnrichmentResult> EnrichAsync(
        string targetIndex, CancellationToken ct);

    // ── Cleanup helpers ──

    /// Deletes lookup entries whose URL doesn't exist in the target index.
    /// Scrolls lookup for URLs, batch-checks against target via _mget, deletes orphans.
    public async Task CleanupOrphanedAsync(
        string targetIndex, CancellationToken ct);

    /// Deletes lookup entries older than the specified age.
    public async Task CleanupOlderThanAsync(
        TimeSpan maxAge, CancellationToken ct);

    /// Purges all entries from the lookup index. Next run regenerates everything.
    public async Task PurgeAsync(CancellationToken ct);
}
```

#### Concurrency: `Task.WhenEach` with proper fallback

```csharp
#if NET9_0_OR_GREATER
await foreach (var completed in Task.WhenEach(tasks).WithCancellation(ct))
{
    var (id, json) = await completed.ConfigureAwait(false);
    // process immediately
}
#else
// HashSet gives O(1) Remove vs List's O(n)
var pending = new HashSet<Task<(string Id, string? Json)>>(tasks);
while (pending.Count > 0)
{
    ct.ThrowIfCancellationRequested();
    var completed = await Task.WhenAny(pending).ConfigureAwait(false);
    pending.Remove(completed);
    var (id, json) = await completed.ConfigureAwait(false);
    // process immediately
}
#endif
```

### 6. Source generator additions — `Elastic.Mapping.Generator`

New files:

| File | Purpose |
|---|---|
| `Model/AiEnrichmentModel.cs` | Model for AI enrichment: inputs, fields, hashes, role |
| `Analysis/AiEnrichmentAnalyzer.cs` | Scans type for `[AiInput]`/`[AiField]`, builds model |
| `Emitters/AiEnrichmentEmitter.cs` | Emits the provider class |

Changes to existing files:

| File | Change |
|---|---|
| `MappingSourceGenerator.cs` | Detect `[AiEnrichment<T>]` on context, call analyzer, pass to emitter |
| `Model/ContextMappingModel.cs` | Add optional `AiEnrichmentModel?` field |

#### `AiEnrichmentModel`

```csharp
internal sealed record AiEnrichmentModel(
    string DocumentTypeName,
    string DocumentTypeFullyQualifiedName,
    string? Role,
    string? LookupIndexName,
    string MatchFieldName,           // "url" — resolved from document type
    ImmutableArray<AiInputField> Inputs,
    ImmutableArray<AiOutputField> Outputs,
    StjContextConfig? StjConfig
);

internal sealed record AiInputField(
    string PropertyName,
    string FieldName              // JSON/ES field name
);

internal sealed record AiOutputField(
    string PropertyName,
    string FieldName,             // JSON/ES field name
    string Description,
    bool IsArray,                 // string vs string[]
    int MinItems,
    int MaxItems,
    string PromptHash             // SHA256 of description, computed at generation time
);
```

#### `AiEnrichmentAnalyzer`

- Scans the document type's properties for `[AiInput]` and `[AiField]` attributes
- Resolves field names from `[JsonPropertyName]` or naming policy (same as `TypeAnalyzer`)
- Computes per-field prompt hashes at generation time
- Returns `AiEnrichmentModel`

#### `AiEnrichmentEmitter`

Emits a nested class on the mapping context:

```csharp
static partial class DocumentationMappingContext
{
    public sealed class DocumentationDocumentAiEnrichmentProvider
        : IAiEnrichmentProvider
    {
        // ── Per-field prompt hashes (compile-time constants) ──
        public IReadOnlyDictionary<string, string> FieldPromptHashes { get; } = ...;
        public IReadOnlyDictionary<string, string> FieldPromptHashFieldNames { get; } = ...;
        public string[] EnrichmentFields { get; } = [...];
        public string[] RequiredSourceFields { get; } = [...];

        // ── BuildPrompt — conditional JSON schema per stale field ──
        public string? BuildPrompt(JsonElement source, IReadOnlyCollection<string> staleFields) { ... }

        // ── ParseResponse — deserialize + re-serialize as partial lookup update ──
        public string? ParseResponse(string llmResponse, IReadOnlyCollection<string> enrichedFields) { ... }

        // ── Lookup infrastructure ──
        public string LookupIndexName => "docs-content-ai-enrichment-cache";
        public string LookupIndexMapping => "...";     // generated from AiOutputFields
        public string MatchField => "url";
        public string EnrichPolicyName => "...";       // versioned by fields hash
        public string EnrichPolicyBody => "...";       // generated
        public string PipelineName => "...";
        public string PipelineBody => "...";           // per-field copy script

        // ── Generated STJ context for AOT-safe parsing ──
        [JsonSerializable(typeof(_AiFields))]
        [JsonSerializable(typeof(_AiPartialUpdate))]
        private sealed partial class _JsonCtx : JsonSerializerContext;

        // Internal types for (de)serialization — only [AiField] properties
        private sealed record _AiFields { ... }
        private sealed record _AiPartialUpdate { ... }
    }

    public static DocumentationDocumentAiEnrichmentProvider AiEnrichment { get; } = new();
}
```

### 7. Fix `OrchestratorContext` — non-nullable secondary

Both aliases are always resolved in `StartAsync`. Make them non-nullable:

```csharp
public class OrchestratorContext<TEvent> where TEvent : class
{
    public IngestSyncStrategy Strategy { get; init; }
    public DateTimeOffset BatchTimestamp { get; init; }
    public string PrimaryWriteAlias { get; init; } = null!;
    public string SecondaryWriteAlias { get; init; } = null!;
    public string PrimaryReadAlias { get; init; } = null!;
    public string SecondaryReadAlias { get; init; } = null!;
}
```

## Integration — how the consumer wires it up

```csharp
var enrichment = new AiEnrichmentOrchestrator(
    transport,
    DocumentationMappingContext.AiEnrichment,
    new AiEnrichmentOptions { MaxEnrichmentsPerRun = 200, MaxConcurrency = 4 }
);

// Secondary type context gets the pipeline as default_pipeline
_semanticTypeContext = ... with
{
    IndexSettings = new Dictionary<string, string>
    {
        ["index.default_pipeline"] = DocumentationMappingContext.AiEnrichment.PipelineName
    }
};

var orchestrator = new IncrementalSyncOrchestrator<DocumentationDocument>(
    transport, _lexicalTypeContext, _semanticTypeContext, ...)
{
    ConfigurePrimary = ConfigureChannelOptions,
    ConfigureSecondary = ConfigureChannelOptions,
    OnPostComplete = async (ctx, _, ct) =>
        await enrichment.EnrichAsync(ctx.SecondaryWriteAlias, ct)
};

orchestrator.AddPreBootstrapTask(async (_, ct) =>
    await enrichment.InitializeAsync(ct));
```

## Post-indexing flow — step by step

```
IncrementalSyncOrchestrator.CompleteAsync()

  1. Drain primary channel
  2. Reindex/Multiplex to secondary
     └─ pipeline applies cached enrichments from lookup
  3. OnPostComplete → enrichment.EnrichAsync(secondaryAlias)

     a. Query secondary for candidates:
        WHERE ai_questions IS NULL
           OR ai_questions_ph != "current_hash"
           OR ai_summary IS NULL
           OR ai_summary_ph != "current_hash"
           OR ...
        _source: [title, stripped_body, ai_questions_ph, ai_summary_ph, ...]
        Paginated with search_after, bounded by MaxEnrichmentsPerRun

     b. For each candidate (WhenEach / WhenAny):
        - Compare per-field _ph values against current hashes
          → staleFields = {"ai_questions"} (only the changed ones)
        - BuildPrompt(source, staleFields) — schema only has stale fields
        - Call _inference/completion endpoint
        - ParseResponse(response, staleFields) → partial JSON
        - POST lookup/_update/{url_hash}
          {"doc": {"ai_questions":[...],"ai_questions_ph":"new"}, "doc_as_upsert": true}

     c. Re-execute enrich policy (rebuild enrich index from lookup)

     d. _update_by_query on secondary:
        WHERE any field missing or stale → re-run pipeline
```

## Cleanup helpers

### `CleanupOrphanedAsync(targetIndex)`

Removes lookup entries for URLs no longer in the target index:

1. Scroll lookup index for all URLs (`_source: ["url"]`)
2. Batch into groups of 1000
3. `_mget` against target index to check existence
4. Collect non-existing URLs
5. `_delete_by_query` on lookup for those URLs

### `CleanupOlderThanAsync(maxAge)`

```json
POST {lookupIndex}/_delete_by_query
{ "query": { "range": { "created_at": { "lt": "now-30d" } } } }
```

### `PurgeAsync()`

```json
POST {lookupIndex}/_delete_by_query
{ "query": { "match_all": {} } }
```

## File inventory

### New files

| File | Package |
|---|---|
| `Attributes/AiEnrichmentAttribute.cs` | `Elastic.Mapping` |
| `Attributes/Fields/AiInputAttribute.cs` | `Elastic.Mapping` |
| `Attributes/Fields/AiFieldAttribute.cs` | `Elastic.Mapping` |
| `Model/AiEnrichmentModel.cs` | `Elastic.Mapping.Generator` |
| `Analysis/AiEnrichmentAnalyzer.cs` | `Elastic.Mapping.Generator` |
| `Emitters/AiEnrichmentEmitter.cs` | `Elastic.Mapping.Generator` |
| `Enrichment/IAiEnrichmentProvider.cs` | `Elastic.Ingest.Elasticsearch` |
| `Enrichment/AiEnrichmentOptions.cs` | `Elastic.Ingest.Elasticsearch` |
| `Enrichment/AiEnrichmentOrchestrator.cs` | `Elastic.Ingest.Elasticsearch` |

### Modified files

| File | Change |
|---|---|
| `MappingSourceGenerator.cs` | Detect `[AiEnrichment<T>]`, call analyzer, emit provider |
| `Model/ContextMappingModel.cs` | Add `AiEnrichmentModel?` to context model |
| `IncrementalSyncOrchestrator.cs` | Make `SecondaryWriteAlias`/`SecondaryReadAlias` non-nullable in `OrchestratorContext` |

## Cost comparison

| Scenario | Single hash | Per-field hash |
|---|---|---|
| Change 1 prompt, 2000 docs | 2000 calls × 5 fields each | 2000 calls × 1 field |
| Add 1 new field, 2000 docs | 2000 calls × 5 fields (all invalidated) | 2000 calls × 1 field |
| No changes, 2000 docs | 0 calls | 0 calls |
| All new docs, 2000 docs | 2000 calls × 5 fields | 2000 calls × 5 fields |
