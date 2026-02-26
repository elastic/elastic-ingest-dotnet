# AI Enrichment

Enrich indexed documents with LLM-generated metadata — summaries, questions, tags — without changing your indexing pipeline. The enrichment runs **post-indexing** as a separate pass, stores results in a persistent lookup index, and backfills via an Elasticsearch enrich policy and ingest pipeline. Changing a prompt only re-generates the affected field.

## How it works

```
                          ┌─────────────────────┐
                          │  Your indexing code  │
                          │  (IngestChannel or   │
                          │   IncrementalSync)   │
                          └─────────┬───────────┘
                                    │ TryWrite(doc)
                                    ▼
                          ┌─────────────────────┐
                          │   Target Index       │
                          │   (ai-docs-secondary)│
                          └─────────┬───────────┘
                                    │
              ┌─────────────────────┼─────────────────────┐
              │         AiEnrichmentOrchestrator           │
              │                                            │
              │  1. Query target for unenriched docs       │
              │  2. Call LLM for each stale field           │
              │  3. Upsert results into lookup index       │
              │  4. Execute enrich policy                  │
              │  5. _update_by_query with pipeline          │
              │         to backfill target                 │
              └──────┬──────────────┬──────────────────────┘
                     │              │
                     ▼              ▼
           ┌──────────────┐  ┌───────────────────┐
           │ Lookup Index  │  │ Inference Endpoint│
           │ (persistent   │  │ (_inference/      │
           │  cache)       │  │  completion/...)   │
           └──────────────┘  └───────────────────┘
```

The lookup index survives index rollovers — when your target index is recreated, the orchestrator detects documents still needing enrichment and backfills them from the existing cache without re-calling the LLM.

## The enrich policy and pipeline

AI enrichment data lives in a **lookup index**, completely separate from your target index. The connection between them is an Elasticsearch [enrich policy](https://www.elastic.co/guide/en/elasticsearch/reference/current/enrich-policy-definition.html) and an [ingest pipeline](https://www.elastic.co/guide/en/elasticsearch/reference/current/ingest.html). Understanding how these work explains why the system is efficient and why changing one prompt doesn't invalidate everything.

### Lookup index

The lookup index stores one document per unique match field value (e.g. one per URL). Each document contains the LLM-generated fields and a prompt hash per field:

```json
{
  "url": "/getting-started",
  "ai_summary": "A guide to installing and configuring Elasticsearch.",
  "ai_summary_ph": "e3b0c442...",
  "ai_questions": ["How do I install?", "How do I configure?", "How do I verify?"],
  "ai_questions_ph": "a1b2c3d4...",
  "created_at": "2026-02-25T10:30:00.000Z"
}
```

The `_ph` (prompt hash) fields are SHA-256 hashes of the `[AiField]` description at compile time. They travel with the data through the enrich policy into the target index, which is how the orchestrator later detects staleness without touching the lookup index.

### Enrich policy

The enrich policy tells Elasticsearch: "given a document's `url` field, look up the matching row in the lookup index and attach its fields." When the orchestrator calls `_enrich/policy/{name}/_execute`, Elasticsearch snapshots the lookup index into a system index optimized for fast point lookups.

### Ingest pipeline

The ingest pipeline runs on every document passing through it. It has two processors:

1. **Enrich processor** — executes the enrich policy, joining the lookup row into a temporary `_enrich` field on the document.
2. **Script processor** — copies each AI field from `_enrich` into the document's top-level fields (only if present), then removes `_enrich`.

The generated script looks like:

```painless
def e = ctx._enrich;
if (e.ai_summary != null) {
  ctx.ai_summary = e.ai_summary;
  ctx.ai_summary_ph = e.ai_summary_ph;
}
if (e.ai_questions != null) {
  ctx.ai_questions = e.ai_questions;
  ctx.ai_questions_ph = e.ai_questions_ph;
}
ctx.remove('_enrich');
```

Because each field is copied independently and carries its own prompt hash, the pipeline applies whatever subset of fields exists in the lookup — it doesn't require all fields to be present.

### How `EnrichAsync` applies enrichments to already-indexed documents

`EnrichAsync` is designed to run **after** indexing completes. Your documents are already in the target index, but they don't yet have AI fields. Here's the sequence:

```
EnrichAsync(targetIndex)
│
├─ 1. Query target index for candidates
│     WHERE ai_summary is missing
│        OR ai_summary_ph != current hash
│        OR ai_questions is missing
│        OR ai_questions_ph != current hash
│
├─ 2. For each candidate, call the LLM
│     Only for the stale fields (not all fields)
│
├─ 3. Bulk upsert results into the lookup index
│     One row per match field value (e.g. per URL)
│
├─ 4. Execute the enrich policy
│     Elasticsearch snapshots the lookup into a fast system index
│
└─ 5. _update_by_query with pipeline on the target index
      Runs the ingest pipeline over every document matching
      the staleness query — the pipeline joins from the
      freshly-snapshotted lookup and writes the AI fields
      directly onto the existing documents
```

Step 5 is the key: `_update_by_query` re-processes documents **in place** through the ingest pipeline. It doesn't re-index them from your source data. It reads each matching document from the target index, runs it through the pipeline (which joins from the lookup), and writes it back with the AI fields populated. Documents that already have current enrichments are not matched by the staleness query and are left untouched.

This means `EnrichAsync` is safe to call repeatedly — it's idempotent. On the first call it processes all unenriched documents up to `MaxEnrichmentsPerRun`. On subsequent calls it only finds documents that are still missing fields or have stale hashes. Once all documents are enriched with current hashes, `EnrichAsync` returns immediately with zero candidates.

## Setup

### 1. Annotate your document

Mark input fields (sent to the LLM) with `[AiInput]` and output fields (generated by the LLM) with `[AiField]`:

```csharp
public partial class DocumentationPage
{
    [Id]
    [Keyword]
    [JsonPropertyName("url")]
    public string Url { get; set; } = null!;

    [AiInput]
    [Text]
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [AiInput]
    [Text]
    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [AiField("A concise two-sentence summary of this documentation page.")]
    [Text]
    [JsonPropertyName("ai_summary")]
    public string? AiSummary { get; set; }

    [AiField("3 to 5 questions this page answers, phrased as a user would ask.",
        MinItems = 3, MaxItems = 5)]
    [Keyword]
    [JsonPropertyName("ai_questions")]
    public string[]? AiQuestions { get; set; }
}
```

### 2. Register on your mapping context

Add an `[AiEnrichment<T>]` attribute alongside your index definitions. The lookup index name is automatically derived from the `WriteAlias` of the first matching `[Index<T>]` — so if your alias contains an environment segment (e.g. `docs-prod-secondary`), the AI cache index will too (`docs-prod-secondary-ai-cache`).

```csharp
[ElasticsearchMappingContext]
[Index<DocumentationPage>(
    Name = "docs-primary",
    Variant = "Primary",
    WriteAlias = "docs-primary",
    ReadAlias = "docs-primary-search",
    DatePattern = "yyyy.MM.dd.HHmmss")]
[Index<DocumentationPage>(
    Name = "docs-secondary",
    Variant = "Secondary",
    WriteAlias = "docs-secondary",
    ReadAlias = "docs-secondary-search",
    DatePattern = "yyyy.MM.dd.HHmmss")]
[AiEnrichment<DocumentationPage>(
    Role = "You are a documentation analysis assistant.",
    MatchField = "url")]
public static partial class MyContext;
```

This generates a `MyContext.AiEnrichment` property implementing `IAiEnrichmentProvider`. The Elasticsearch resources are named from the `WriteAlias`:

- Lookup index: `docs-secondary-ai-cache`
- Enrich policy: `docs-secondary-ai-cache-ai-policy`
- Pipeline: `docs-secondary-ai-cache-ai-pipeline`

The generated provider includes:

- Prompt building (JSON schema constrained, per-field)
- Response parsing (extracts fields + writes prompt hashes)
- Lookup index mapping, enrich policy, and ingest pipeline JSON

### 3. Wire up the orchestrator

The `AiEnrichmentOrchestrator` needs an `ITransport` and the generated provider. Use it in two phases: **initialize** before indexing, **enrich** after.

## Minimal setup with IngestChannel

```csharp
var transport = /* your ITransport */;
var typeContext = MyContext.DocumentationPageSecondary.Context;

// ── Initialize AI infrastructure ──
using var enrichment = new AiEnrichmentOrchestrator(transport, MyContext.AiEnrichment);
await enrichment.InitializeAsync();

// ── Index documents ──
var options = new IngestChannelOptions<DocumentationPage>(transport, typeContext);
using var channel = new IngestChannel<DocumentationPage>(options);
await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

foreach (var page in pages)
    channel.TryWrite(page);

await channel.WaitForDrainAsync(TimeSpan.FromSeconds(30));

// ── Enrich ──
var targetIndex = typeContext.ResolveWriteAlias();
var result = await enrichment.EnrichAsync(targetIndex);

Console.WriteLine($"Enriched: {result.Enriched}, Failed: {result.Failed}, " +
                  $"Skipped: {result.Skipped}, ReachedLimit: {result.ReachedLimit}");
```

## Minimal setup with IncrementalSyncOrchestrator

The `IncrementalSyncOrchestrator` manages a primary/secondary index pair with automatic reindexing. Hook AI initialization into its pre-bootstrap phase:

```csharp
var transport = /* your ITransport */;

// ── Create the AI orchestrator ──
using var enrichment = new AiEnrichmentOrchestrator(
    transport,
    MyContext.AiEnrichment,
    new AiEnrichmentOptions { MaxEnrichmentsPerRun = 200 });

// ── Create the sync orchestrator ──
using var sync = new IncrementalSyncOrchestrator<DocumentationPage>(
    transport,
    MyContext.DocumentationPagePrimary,
    MyContext.DocumentationPageSecondary);

// Hook AI initialization into the bootstrap phase
sync.AddPreBootstrapTask(async (t, ct) =>
{
    await enrichment.InitializeAsync(ct);
});

// ── Bootstrap and index ──
await sync.StartAsync(BootstrapMethod.Failure);

foreach (var page in pages)
    sync.TryWrite(page);

await sync.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(30));

// ── Enrich the secondary (live) index ──
var secondaryAlias = MyContext.DocumentationPageSecondary.Context.ResolveWriteAlias();
var result = await enrichment.EnrichAsync(secondaryAlias);

// ── Optional: clean up stale cache entries ──
await enrichment.CleanupOlderThanAsync(TimeSpan.FromDays(30));
await enrichment.CleanupOrphanedAsync(secondaryAlias);
```

## Configuration

`AiEnrichmentOptions` controls runtime behavior:

| Property | Default | Description |
|---|---|---|
| `MaxEnrichmentsPerRun` | 100 | Hard cap per run. Coverage grows across repeated deploys. |
| `MaxConcurrency` | 4 | Concurrent LLM inference calls via `SemaphoreSlim`. |
| `QueryBatchSize` | 50 | Documents fetched per `search_after` page. |
| `InferenceEndpointId` | `.gp-llm-v2-completion` | Elasticsearch inference endpoint for `_inference/completion`. |

## Per-field prompt hashing

Each `[AiField]` description is SHA-256 hashed at compile time. The hash is stored alongside the enriched value in both the lookup index and the target index (as `{field}_ph` fields). When you change a field's description, only that field is regenerated — other fields keep their cached values.

```
Document in target:  { "ai_summary_ph": "abc123", "ai_questions_ph": "def456" }
Current hashes:      { "ai_summary": "abc123",    "ai_questions": "xyz789" }
                                     ✓ current                    ✗ stale
→ Only ai_questions is sent to the LLM
```

This works because the prompt hash travels through the entire pipeline:

1. **Source gen** computes a SHA-256 of the `[AiField("...")]` description string.
2. **LLM response parsing** writes the field value *and* its hash into the lookup index: `{"ai_questions": [...], "ai_questions_ph": "xyz789"}`.
3. **Enrich policy** joins these into the target document.
4. **Ingest pipeline** copies both the field and its `_ph` companion onto the target document.
5. **Next `EnrichAsync` call** queries the target index for documents where any `_ph` field doesn't match the current hash — only those documents are candidates, and only the stale fields within each candidate are sent to the LLM.

This means you can evolve your prompts incrementally. If you change the description on `ai_summary` but leave `ai_questions` alone, only `ai_summary` is regenerated across all documents. The `ai_questions` values and their hashes remain untouched in both the lookup and target indices.

## Lifecycle methods

| Method | When to call | What it does |
|---|---|---|
| `InitializeAsync` | Before indexing | Creates lookup index, enrich policy, executes policy, creates pipeline |
| `EnrichAsync(targetIndex)` | After indexing | Finds candidates, calls LLM, upserts lookup, backfills target |
| `CleanupOrphanedAsync(targetIndex)` | Periodic maintenance | Deletes lookup entries whose match value no longer exists in the target |
| `CleanupOlderThanAsync(maxAge)` | Periodic maintenance | Deletes lookup entries older than the specified duration |
| `PurgeAsync` | Full reset | Deletes all lookup entries |

## Elasticsearch resources created

For a document type `DocumentationPage` registered with `[Index<DocumentationPage>(WriteAlias = "docs-secondary", ...)]` and AI fields `ai_summary` + `ai_questions`, the orchestrator manages:

| Resource | Name pattern | Example | Purpose |
|---|---|---|---|
| Lookup index | `{write-alias}-ai-cache` | `docs-secondary-ai-cache` | Persistent store for LLM results |
| Enrich policy | `{lookup-index}-ai-policy` | `docs-secondary-ai-cache-ai-policy` | Joins lookup data into documents at ingest time |
| Ingest pipeline | `{lookup-index}-ai-pipeline` | `docs-secondary-ai-cache-ai-pipeline` | Runs the enrich processor + copies fields via script |

The lookup index name is derived from the `WriteAlias` of the `[Index<T>]` registration for the same document type, which naturally includes any environment prefix you use. If no `WriteAlias` is set, it falls back to the lowercase document type name. You can also override it explicitly via `LookupIndexName` on the `[AiEnrichment<T>]` attribute.

### Stable names, conditional updates

The policy and pipeline names are **stable** — they don't change when you add or remove AI fields. Instead, the orchestrator stores a fields hash in the pipeline description (e.g. `[fields_hash:a1b2c3d4]`) and conditionally updates the policy/pipeline only when the hash changes. This avoids accumulating stale resources in the cluster.

### One AI enrichment per entity

Only one `[AiEnrichment<T>]` is allowed per document type `T` across all mapping contexts. The source generator emits a compile-time error (`ELASTIC001`) if duplicates are detected.
