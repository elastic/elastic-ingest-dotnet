---
navigation_title: Legacy channels
---

# Legacy channel migration

The specialized channel types (`DataStreamChannel`, `IndexChannel`, `CatalogChannel`, and semantic channel patterns) are superseded by `IngestChannel<T>` with composable strategies. This page shows how to migrate each legacy pattern.

## DataStreamChannel -> IngestChannel

**Before (deprecated):**

```csharp
var channel = new DataStreamChannel<LogEvent>(
    new DataStreamChannelOptions<LogEvent>(transport)
    {
        DataStream = new DataStreamName("logs", "myapp", "default")
    }
);
```

**After:**

```csharp
[ElasticsearchMappingContext]
[DataStream<LogEvent>(
    Type = "logs",
    Dataset = "myapp",
    Namespace = "default"
)]
public static partial class LogContext;

var options = new IngestChannelOptions<LogEvent>(transport, LogContext.LogEvent.Context);
using var channel = new IngestChannel<LogEvent>(options);
```

The channel auto-selects `DataStreamIngestStrategy` and creates data stream templates during bootstrap.

## IndexChannel -> IngestChannel

**Before (deprecated):**

```csharp
var channel = new IndexChannel<Product>(
    new IndexChannelOptions<Product>(transport)
    {
        IndexFormat = "products-{0:yyyy.MM.dd}",
        TimestampLookup = p => p.UpdatedAt
    }
);
```

**After:**

```csharp
[ElasticsearchMappingContext]
[Index<Product>(
    Name = "products",
    DatePattern = "yyyy.MM.dd"
)]
public static partial class ProductContext;

var options = new IngestChannelOptions<Product>(transport, ProductContext.Product.Context);
using var channel = new IngestChannel<Product>(options);
```

The `[Timestamp]` attribute on your document type replaces `TimestampLookup`. The `DatePattern` replaces the format string.

## CatalogChannel -> IngestChannel

**Before (deprecated):**

```csharp
var options = new IngestChannelOptions<Product>(transport, MyContext.Product);
options.IngestStrategy = new CatalogIngestStrategy<Product>(
    MyContext.Product, "products", options.BulkPathAndQuery
);
var channel = new IngestChannel<Product>(options);
```

**After:**

```csharp
[ElasticsearchMappingContext]
[Index<Product>(
    Name = "products",
    WriteAlias = "products",
    ReadAlias = "products-search",
    DatePattern = "yyyy.MM.dd.HHmmss"
)]
public static partial class CatalogContext;

var options = new IngestChannelOptions<Product>(transport, CatalogContext.Product.Context);
using var channel = new IngestChannel<Product>(options);
```

With `[Id]` on the document, the `TypeContextIndexIngestStrategy` uses `index` operations (upserts) automatically. Add `[ContentHash]` for hash-based provisioning, and configure `WriteAlias`/`ReadAlias` for alias management.

## Semantic channel -> IngestChannel

**Before (deprecated):**

```csharp
var options = new IngestChannelOptions<Article>(transport, MyContext.Article);
options.BootstrapStrategy = new DefaultBootstrapStrategy(
    new InferenceEndpointStep("my-elser-endpoint", numThreads: 2),
    new ComponentTemplateStep(),
    new DataStreamTemplateStep()
);
var channel = new IngestChannel<Article>(options);
```

**After:**

The semantic channel was already using `IngestChannel<T>` with `InferenceEndpointStep`. Use the `IngestStrategies` factory and pass a custom bootstrap strategy:

```csharp
var bootstrap = new DefaultBootstrapStrategy(
    new InferenceEndpointStep("my-elser-endpoint", numThreads: 2),
    new ComponentTemplateStep(),
    new IndexTemplateStep()
);
var strategy = IngestStrategies.Index<Article>(
    MyContext.Article.Context, bootstrap);
var options = new IngestChannelOptions<Article>(transport, strategy,
    MyContext.Article.Context);
using var channel = new IngestChannel<Article>(options);
```

## Key differences

| Feature | Legacy channels | IngestChannel&lt;T&gt; |
|---------|----------------|-------------------------------|
| Strategy configuration | Built-in, fixed | Composable, pluggable |
| Auto-configuration | None | From `ElasticsearchTypeContext` |
| Provisioning | Manual | Hash-based reuse (automatic) |
| Alias management | Manual | `LatestAndSearchAliasStrategy` (automatic) |
| Rollover | Not supported | `ManualRolloverStrategy` |
| Orchestration | Not supported | `IncrementalSyncOrchestrator` |
