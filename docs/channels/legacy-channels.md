---
navigation_title: Legacy channels
---

# Legacy channel migration

The specialized channel types (`DataStreamChannel`, `IndexChannel`, `CatalogChannel`, and semantic channel patterns) are superseded by `IngestChannel<T>` with composable strategies. This page shows how to migrate each legacy pattern.

## DataStreamChannel &rarr; IngestChannel

**Before:**

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
[Entity<LogEvent>(
    Target = EntityTarget.DataStream,
    DataStreamType = "logs",
    DataStreamDataset = "myapp",
    DataStreamNamespace = "default"
)]
public static partial class LogContext;

var options = new IngestChannelOptions<LogEvent>(transport, LogContext.LogEvent.Context);
using var channel = new IngestChannel<LogEvent>(options);
```

The channel auto-selects `DataStreamIngestStrategy` and creates data stream templates during bootstrap.

## IndexChannel &rarr; IngestChannel

**Before:**

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
[Entity<Product>(
    Target = EntityTarget.Index,
    Name = "products",
    DatePattern = "yyyy.MM.dd"
)]
public static partial class ProductContext;

var options = new IngestChannelOptions<Product>(transport, ProductContext.Product.Context);
using var channel = new IngestChannel<Product>(options);
```

The `[Timestamp]` attribute on your document type replaces `TimestampLookup`. The `DatePattern` replaces the format string.

## CatalogChannel &rarr; IngestChannel

**Before:**

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
[Entity<Product>(
    Target = EntityTarget.Index,
    Name = "products",
    WriteAlias = "products",
    ReadAlias = "products-search",
    SearchPattern = "products-*",
    DatePattern = "yyyy.MM.dd.HHmmss"
)]
public static partial class CatalogContext;

var options = new IngestChannelOptions<Product>(transport, CatalogContext.Product.Context);
using var channel = new IngestChannel<Product>(options);
```

With `[Id]` on the document, the `TypeContextIndexIngestStrategy` uses `index` operations (upserts) automatically. Add `[ContentHash]` for hash-based provisioning, and configure `WriteAlias`/`ReadAlias` for alias management.

## Semantic channel &rarr; IngestChannel

**Before:**

```csharp
var options = new IngestChannelOptions<Article>(transport, MyContext.Article);
options.BootstrapStrategy = new DefaultBootstrapStrategy(
    new InferenceEndpointStep("my-elser-endpoint", numThreads: 2),
    new ComponentTemplateStep(),
    new DataStreamTemplateStep()
);
var channel = new IngestChannel<Article>(options);
```

**After (no change needed):**

The semantic channel was already using `IngestChannel<T>` with `InferenceEndpointStep`. This pattern remains the recommended approach. Add the step to your bootstrap strategy when you need inference endpoints:

```csharp
var options = new IngestChannelOptions<Article>(transport, MyContext.Article.Context)
{
    BootstrapStrategy = new DefaultBootstrapStrategy(
        new InferenceEndpointStep("my-elser-endpoint", numThreads: 2),
        new ComponentTemplateStep(),
        new IndexTemplateStep()
    )
};
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
