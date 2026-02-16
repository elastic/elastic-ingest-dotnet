---
navigation_title: Mapping context
---

# Mapping context and strategy resolution

The `[Entity<>]` attribute on your mapping context drives everything the channel does: what kind of Elasticsearch target to create, which templates to put in place, how to format bulk operations, and how to manage aliases. This page is the complete reference for how `[Entity<>]` parameters map to channel behavior.

## Simplest form

```csharp
[ElasticsearchMappingContext]
[Entity<Product>]
public static partial class MyContext;
```

With no parameters, `[Entity<Product>]` defaults to:
- **Target**: `EntityTarget.Index`
- **Name**: `product` (type name, lowercased)
- No aliases, no date pattern, no data stream settings

## Entity targets

The `Target` parameter determines the fundamental ingestion strategy:

```csharp
// Index (default) -- mutable documents, upserts, aliases
[Entity<Product>]
[Entity<Product>(Target = EntityTarget.Index)]

// Data stream -- append-only, automatic rollover
[Entity<LogEntry>(Target = EntityTarget.DataStream,
    DataStreamType = "logs", DataStreamDataset = "myapp")]

// Wired stream -- serverless managed, no local bootstrap
[Entity<LogEntry>(Target = EntityTarget.WiredStream)]
```

### Strategy resolution by target

| | Index | DataStream | WiredStream |
|---|---|---|---|
| **Ingest** | `TypeContextIndexIngestStrategy` | `DataStreamIngestStrategy` | `WiredStreamIngestStrategy` |
| **Bulk operation** | `index` (upsert) | `create` (append) | `create` (append) |
| **Bootstrap** | Component + index templates | Component + data stream templates | No-op |
| **Provisioning** | Hash-based reuse or always create | Always create | Always create |
| **Alias** | Configured or none | None | None |

## Document attributes

Attributes on your document class further refine the resolved strategy:

| Attribute | Effect on strategy |
|-----------|-------------------|
| `[Id]` | Bulk headers include `_id` field, enabling upserts instead of blind inserts |
| `[Timestamp]` | Required for data streams. Used for date-based index naming when `DatePattern` is set |
| `[ContentHash]` | Enables `HashBasedReuseProvisioning` -- the channel checks if the existing index has the same schema hash and reuses it instead of creating a new one |
| `[Keyword]` | Maps to `keyword` field type in the component template |
| `[Text]` | Maps to `text` field type in the component template |
| `[Dimension]` | Marks a TSDB dimension field (requires `DataStreamMode = DataStreamMode.Tsdb`) |

## Index parameters

For `EntityTarget.Index`, these parameters control index naming and alias management:

```csharp
[Entity<Product>(
    Target = EntityTarget.Index,
    Name = "products",                    // Index name (default: type name lowercased)
    WriteAlias = "products",              // Write alias for zero-downtime rotation
    ReadAlias = "products-search",        // Search alias spanning all indices
    SearchPattern = "products-*",         // Glob pattern matching all indices
    DatePattern = "yyyy.MM.dd.HHmmss"    // Time-stamped index names
)]
```

| Parameter | Default | Effect |
|-----------|---------|--------|
| `Name` | Type name lowercased | Base index name |
| `WriteAlias` | None | Enables `LatestAndSearchAliasStrategy` |
| `ReadAlias` | None | Search alias spanning all matching indices |
| `SearchPattern` | None | Glob for matching indices in alias operations |
| `DatePattern` | None | Creates time-stamped index names (e.g. `products-2026.02.15.120000`) |
| `Shards` | -1 (omitted) | Number of primary shards |
| `Replicas` | -1 (omitted) | Number of replica shards |
| `RefreshInterval` | None | Index refresh interval |

### Alias strategy resolution

| WriteAlias | ReadAlias | Strategy |
|-----------|-----------|----------|
| Not set | Not set | `NoAliasStrategy` |
| Set | Set | `LatestAndSearchAliasStrategy` -- swaps write alias to latest index, search alias spans all |

## Data stream parameters

For `EntityTarget.DataStream`, these parameters control the data stream naming convention `{type}-{dataset}-{namespace}`:

```csharp
[Entity<LogEntry>(
    Target = EntityTarget.DataStream,
    DataStreamType = "logs",              // Required: logs, metrics, traces, etc.
    DataStreamDataset = "myapp",          // Required: source identifier
    DataStreamNamespace = "production",   // Default: "default"
    DataStreamMode = DataStreamMode.Tsdb  // Default: DataStreamMode.Default
)]
```

| Parameter | Default | Effect |
|-----------|---------|--------|
| `DataStreamType` | Required | Data category (`logs`, `metrics`, `traces`) |
| `DataStreamDataset` | Required | Source identifier |
| `DataStreamNamespace` | `"default"` | Environment (`production`, `staging`) |
| `DataStreamMode` | `Default` | `Default`, `LogsDb`, or `Tsdb` |

Data stream name: `{type}-{dataset}-{namespace}` (e.g. `logs-myapp-production`)

## Variants

Use `Variant` to define multiple index configurations for the same document type:

```csharp
[ElasticsearchMappingContext]
[Entity<Article>(
    Target = EntityTarget.Index,
    Name = "articles-lexical",
    WriteAlias = "articles-lexical",
    ReadAlias = "articles-lexical-search",
    SearchPattern = "articles-lexical-*",
    DatePattern = "yyyy.MM.dd.HHmmss"
)]
[Entity<Article>(
    Target = EntityTarget.Index,
    Name = "articles-semantic",
    Variant = "Semantic",
    WriteAlias = "articles-semantic",
    ReadAlias = "articles-semantic-search",
    SearchPattern = "articles-semantic-*",
    DatePattern = "yyyy.MM.dd.HHmmss"
)]
public static partial class ArticleContext;
```

This generates:
- `ArticleContext.Article.Context` (lexical, default variant)
- `ArticleContext.ArticleSemantic.Context` (semantic variant)

Use variants with `IncrementalSyncOrchestrator` to coordinate multiple indices over the same data. See [semantic enrichment](../use-cases/semantic-enrichment.md) for an end-to-end example.

## Overriding inferred strategies

When the auto-resolved strategy isn't enough, use the `IngestStrategies` and `BootstrapStrategies` factory methods:

```csharp
// Add 30-day retention to a data stream
var strategy = IngestStrategies.DataStream<LogEntry>(context, "30d");
var options = new IngestChannelOptions<LogEntry>(transport, strategy, context);

// Add ILM to an index
var strategy = IngestStrategies.Index<Product>(context,
    BootstrapStrategies.IndexWithIlm("products-policy"));
var options = new IngestChannelOptions<Product>(transport, strategy, context);
```

See [strategies](../strategies/index.md) for the complete factory method reference.
