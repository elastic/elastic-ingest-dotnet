---
navigation_title: Mapping context
---

# Mapping context and strategy resolution

The target-specific attributes -- `[Index<T>]`, `[DataStream<T>]`, and `[WiredStream<T>]` -- on your mapping context drive everything the channel does: what kind of Elasticsearch target to create, which templates to put in place, how to format bulk operations, and how to manage aliases. This page is the complete reference for how attribute parameters map to channel behavior.

## Simplest form

```csharp
[ElasticsearchMappingContext]
[Index<Product>(Name = "products")]
public static partial class MyContext;
```

This targets an index named `products`. The source generator produces `MyContext.Product.Context` -- an `ElasticsearchTypeContext` containing mappings JSON, settings, and accessor delegates.

## Target-specific attributes

Each Elasticsearch target type has its own attribute with properties that make sense for that target:

```csharp
// Index -- mutable documents, upserts, aliases
[Index<Product>(Name = "products")]

// Index with templated name -- resolved at runtime
[Index<Product>(NameTemplate = "products-{env}")]

// Data stream -- append-only, automatic rollover
[DataStream<LogEntry>(Type = "logs", Dataset = "myapp")]

// Wired stream -- serverless managed, no local bootstrap
[WiredStream<LogEntry>(Type = "logs", Dataset = "myapp")]
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

`[Index<T>]` supports the following properties:

```csharp
[Index<Product>(
    Name = "products",                    // Fixed index name
    WriteAlias = "products-write",        // Write alias for zero-downtime rotation
    ReadAlias = "products-search",        // Search alias spanning all indices
    DatePattern = "yyyy.MM.dd.HHmmss",   // Time-stamped index names
    Shards = 3,
    Replicas = 2,
    RefreshInterval = "5s",
    Dynamic = false
)]
```

| Parameter | Default | Effect |
|-----------|---------|--------|
| `Name` | Type name lowercased | Fixed index name (mutually exclusive with `NameTemplate`) |
| `NameTemplate` | None | Runtime-parameterized index name (see [templated index names](templated-index-names.md)) |
| `WriteAlias` | None | Enables `LatestAndSearchAliasStrategy` |
| `ReadAlias` | None | Search alias spanning all matching indices |
| `DatePattern` | None | Creates time-stamped index names (e.g. `products-2026.02.15.120000`) and auto-derives search pattern |
| `Shards` | -1 (omitted) | Number of primary shards |
| `Replicas` | -1 (omitted) | Number of replica shards |
| `RefreshInterval` | None | Index refresh interval |
| `Dynamic` | `true` | When `false`, unmapped fields are silently ignored |
| `Configuration` | None | Static class with `ConfigureAnalysis`/`ConfigureMappings` methods |
| `Variant` | None | Registers multiple configurations for the same type |

### Alias strategy resolution

| WriteAlias | ReadAlias | Strategy |
|-----------|-----------|----------|
| Not set | Not set | `NoAliasStrategy` |
| Set | Set | `LatestAndSearchAliasStrategy` -- swaps write alias to latest index, search alias spans all |

### SearchPattern auto-derivation

`SearchPattern` is no longer a manual parameter. When `DatePattern` is set, the search pattern is auto-derived as `"{writeTarget}-*"`. When `DatePattern` is not set, no search pattern is generated.

## Templated index names

Use `NameTemplate` for index names that depend on runtime parameters:

```csharp
[Index<KnowledgeArticle>(
    NameTemplate = "docs-{searchType}-{env}",
    DatePattern = "yyyy.MM.dd.HHmmss"
)]
```

This generates `CreateContext(string searchType, string? env = null)` instead of a static `Context` property. The `{env}`, `{environment}`, and `{namespace}` placeholders are optional and resolve from environment variables when omitted.

See [templated index names](templated-index-names.md) for the full reference.

## Data stream parameters

`[DataStream<T>]` controls the data stream naming convention `{type}-{dataset}-{namespace}`:

```csharp
[DataStream<LogEntry>(
    Type = "logs",                        // Required: logs, metrics, traces, etc.
    Dataset = "myapp",                    // Required: source identifier
    Namespace = "production",             // Optional: defaults from environment variables
    DataStreamMode = DataStreamMode.Tsdb  // Optional: Default, LogsDb, or Tsdb
)]
```

| Parameter | Default | Effect |
|-----------|---------|--------|
| `Type` | Required | Data category (`logs`, `metrics`, `traces`) |
| `Dataset` | Required | Source identifier |
| `Namespace` | Environment variable | Environment (`production`, `staging`). Resolves from `DOTNET_ENVIRONMENT` > `ASPNETCORE_ENVIRONMENT` > `ENVIRONMENT` > `"development"` when omitted |
| `DataStreamMode` | `Default` | `Default`, `LogsDb`, or `Tsdb` |
| `Configuration` | None | Static class with `ConfigureAnalysis`/`ConfigureMappings` methods |
| `Variant` | None | Registers multiple configurations for the same type |

Data stream name: `{type}-{dataset}-{namespace}` (e.g. `logs-myapp-production`)

## Wired stream parameters

`[WiredStream<T>]` is similar to `[DataStream<T>]` but bootstrap is fully managed by Elasticsearch:

```csharp
[WiredStream<LogEntry>(
    Type = "logs",
    Dataset = "myapp"
)]
```

| Parameter | Default | Effect |
|-----------|---------|--------|
| `Type` | Required | Data category |
| `Dataset` | Required | Source identifier |
| `Namespace` | Environment variable | Resolved from environment when omitted |
| `Configuration` | None | Static class with configuration methods |
| `Variant` | None | Registers multiple configurations for the same type |

## Variants

Use `Variant` to define multiple index configurations for the same document type:

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
