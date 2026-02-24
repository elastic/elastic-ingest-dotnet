---
navigation_title: Semantic enrichment
---

# Incremental sync with semantic enrichment

This use case shows how to maintain a lexical search index and a semantic search index over the same data, using `IncrementalSyncOrchestrator` to coordinate both channels. The semantic index uses an ELSER inference endpoint for embedding generation.

## When to use

- You have reference data (articles, products, documentation) that needs both keyword and semantic search
- You want zero-downtime schema changes with automatic reindex-vs-multiplex detection
- You need coordinated alias swapping across multiple indices

## Document type

```csharp
public class Article
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

## Mapping context with variants

Define two index configurations -- lexical and semantic -- using the `Variant` parameter:

```csharp
[ElasticsearchMappingContext]
[Index<Article>(
    Name = "articles-lexical",
    WriteAlias = "articles-lexical",
    ReadAlias = "articles-lexical-search",
    DatePattern = "yyyy.MM.dd.HHmmss"
)]
[Index<Article>(
    Name = "articles-semantic",
    Variant = "Semantic",
    WriteAlias = "articles-semantic",
    ReadAlias = "articles-semantic-search",
    DatePattern = "yyyy.MM.dd.HHmmss"
)]
public static partial class ArticleContext;
```

This generates:
- `ArticleContext.Article.Context` -- lexical index
- `ArticleContext.ArticleSemantic.Context` -- semantic index

## Orchestrator setup

```csharp
var transport = new DistributedTransport(
    new TransportConfiguration(new Uri("http://localhost:9200"))
);

using var orchestrator = new IncrementalSyncOrchestrator<Article>(
    transport,
    primary: ArticleContext.Article.Context,
    secondary: ArticleContext.ArticleSemantic.Context
);
```

## Adding an inference endpoint

Use a pre-bootstrap task to create the ELSER inference endpoint before the semantic index template is created:

```csharp
orchestrator.AddPreBootstrapTask(async (transport, ctx) =>
{
    // Create ELSER inference endpoint for semantic embeddings
    var body = """
    {
        "service": "elser",
        "service_settings": {
            "num_allocations": 1,
            "num_threads": 2
        }
    }
    """;
    await transport.PutAsync<StringResponse>(
        "_inference/sparse_embedding/article-elser",
        PostData.String(body), cancellationToken: ctx);
});
```

Alternatively, add the inference endpoint as a bootstrap step on the secondary channel:

```csharp
orchestrator.ConfigureSecondary = opts =>
{
    // The secondary channel needs a custom bootstrap that includes the inference endpoint
};
```

## Running the sync

```csharp
var strategy = await orchestrator.StartAsync(BootstrapMethod.Failure);
Console.WriteLine($"Strategy: {strategy}"); // Reindex or Multiplex

// Write documents -- the orchestrator routes to the right channels
foreach (var article in await GetArticlesFromSource())
    orchestrator.TryWrite(article);

// Drain, reindex/multiplex, alias swap, cleanup
var success = await orchestrator.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(60));
```

## Strategy selection

The orchestrator automatically chooses between:

- **Reindex mode**: both index schemas are unchanged (template hashes match). Only the primary channel receives writes; after drain, documents are reindexed to the secondary. This is faster because documents are only serialized and sent once.
- **Multiplex mode**: a schema has changed or the secondary index doesn't exist. Both channels receive every document simultaneously. This handles schema differences by writing directly to both indices.

## Post-completion hook

Track sync results or trigger downstream processes:

```csharp
orchestrator.OnPostComplete = async (context, ctx) =>
{
    Console.WriteLine($"Strategy used: {context.Strategy}");
    Console.WriteLine($"Batch timestamp: {context.BatchTimestamp}");
    // Notify search service to refresh caches, update metrics, etc.
};
```

## Related

- [Incremental sync](../orchestration/incremental-sync.md): detailed orchestration workflow with mermaid diagrams
- [Catalog data](catalog-data.md): dual-index orchestration without inference
- [Custom strategies](../advanced/custom-strategies.md): adding custom bootstrap steps
- [Mapping context](../getting-started/mapping-context.md): variant configuration
