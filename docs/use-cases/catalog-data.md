---
navigation_title: Catalog data
---

# Catalog data use case

This guide covers versioned reference data with dual-index orchestration -- for example, a knowledge base where you maintain both a lexical search index and a semantic search index.

## Why

Versioned snapshots of reference data need zero-downtime schema changes. When you maintain multiple search indices over the same data (for example, lexical and semantic), you need a way to coordinate writes, detect schema changes, and swap aliases atomically -- without manual orchestration code.

## Scenario

- Reference data is synced periodically from a source system
- Each document has a stable ID and a content hash for deduplication
- Two indices are maintained: one for lexical search, one for semantic search (different mappings)
- Index swaps should be atomic via aliases
- Unchanged schemas should reuse existing indices (no unnecessary reindexing)

## Single-channel pattern

Start with a single index before adding orchestration complexity:

```csharp
[ElasticsearchMappingContext]
[Entity<KnowledgeArticle>(
    Target = EntityTarget.Index,
    Name = "knowledge",
    WriteAlias = "knowledge",
    ReadAlias = "knowledge-search",
    SearchPattern = "knowledge-*",
    DatePattern = "yyyy.MM.dd.HHmmss"
)]
public static partial class KnowledgeContext;

var options = new IngestChannelOptions<KnowledgeArticle>(transport, KnowledgeContext.KnowledgeArticle.Context);
using var channel = new IngestChannel<KnowledgeArticle>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

foreach (var article in await GetArticlesFromSource())
    channel.TryWrite(article);

await channel.WaitForDrainAsync(TimeSpan.FromSeconds(30), ctx);
await channel.ApplyAliasesAsync(string.Empty, ctx);
```

This gives you hash-based index reuse, alias swapping, and upserts -- all from the entity declaration.

## Document type

```csharp
public class KnowledgeArticle
{
    [Id]
    [Keyword]
    public string Url { get; set; }

    [Text(Analyzer = "standard")]
    public string Title { get; set; }

    [Text(Analyzer = "standard")]
    public string Body { get; set; }

    [ContentHash]
    [Keyword]
    public string Hash { get; set; }

    [Timestamp]
    [JsonPropertyName("@timestamp")]
    public DateTimeOffset UpdatedAt { get; set; }
}
```

## Dual-index orchestration

When you need a second index with different mappings (for example, semantic search), use `IncrementalSyncOrchestrator` to coordinate both indices automatically.

### Mapping context with variants

Use the `Variant` parameter to define multiple index configurations for the same document type:

```csharp
[ElasticsearchMappingContext]
[Entity<KnowledgeArticle>(
    Target = EntityTarget.Index,
    Name = "knowledge-lexical",
    WriteAlias = "knowledge-lexical",
    ReadAlias = "knowledge-lexical-search",
    SearchPattern = "knowledge-lexical-*",
    DatePattern = "yyyy.MM.dd.HHmmss"
)]
[Entity<KnowledgeArticle>(
    Target = EntityTarget.Index,
    Name = "knowledge-semantic",
    Variant = "Semantic",
    WriteAlias = "knowledge-semantic",
    ReadAlias = "knowledge-semantic-search",
    SearchPattern = "knowledge-semantic-*",
    DatePattern = "yyyy.MM.dd.HHmmss"
)]
public static partial class ExampleMappingContext;
```

This generates two type contexts:
- `ExampleMappingContext.KnowledgeArticle.Context` (lexical)
- `ExampleMappingContext.KnowledgeArticleSemantic.Context` (semantic variant)

### IncrementalSyncOrchestrator

The orchestrator coordinates both indices, handling schema change detection and the decision between reindex and multiplex modes automatically:

```csharp
var transport = new DistributedTransport(
    new TransportConfiguration(new Uri("http://localhost:9200"))
);

using var orchestrator = new IncrementalSyncOrchestrator<KnowledgeArticle>(
    transport,
    primary: ExampleMappingContext.KnowledgeArticle.Context,
    secondary: ExampleMappingContext.KnowledgeArticleSemantic.Context
);

// Optional: add tasks that run before channel bootstrap
orchestrator.AddPreBootstrapTask(async (transport, ctx) =>
{
    // Create synonym sets, query rules, or other prerequisites
});

var strategy = await orchestrator.StartAsync(BootstrapMethod.Failure);
Console.WriteLine($"Strategy: {strategy}"); // Reindex or Multiplex

// Write documents -- the orchestrator routes to the right channels
foreach (var article in await GetArticlesFromSource())
    orchestrator.TryWrite(article);

// Drain, reindex/multiplex, alias swap, cleanup
var success = await orchestrator.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(30));
```

### Strategy selection

The orchestrator automatically selects the strategy:

- **Reindex**: when both index schemas are unchanged (template hashes match and secondary alias exists). Only the primary channel receives writes; the secondary is updated via `_reindex` after drain.
- **Multiplex**: when any schema has changed or the secondary index doesn't exist yet. Both channels receive every document simultaneously.

See [incremental sync](../orchestration/incremental-sync.md) for detailed diagrams of both modes.

### Post-completion hook

Run custom logic after orchestration completes:

```csharp
orchestrator.OnPostComplete = async (context, ctx) =>
{
    Console.WriteLine($"Strategy used: {context.Strategy}");
    Console.WriteLine($"Batch timestamp: {context.BatchTimestamp}");
    // Trigger downstream processes, send notifications, etc.
};
```

## What auto-configures

Because the entity declarations include `WriteAlias`, `ReadAlias`, `SearchPattern`, `DatePattern`, and `[ContentHash]`, the channels automatically use:

| Behavior | Strategy |
|----------|----------|
| Ingest | `TypeContextIndexIngestStrategy` -- uses `[Id]` for upserts |
| Bootstrap | `DefaultBootstrapStrategy` with component + index templates |
| Provisioning | `HashBasedReuseProvisioning` -- reuses index if content hash matches |
| Alias | `LatestAndSearchAliasStrategy` -- manages write and search aliases |

## Related

- [Incremental sync](../orchestration/incremental-sync.md): detailed orchestration workflow with mermaid diagrams
- [Provisioning strategies](../strategies/provisioning.md): how hash-based reuse works
- [Alias strategies](../strategies/alias.md): how alias swapping works
