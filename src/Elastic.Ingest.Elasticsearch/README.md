# Elastic.Ingest.Elasticsearch

Production-ready bulk ingestion into Elasticsearch — batching, backpressure, retries, and index management handled for you.

## Install

```shell
dotnet add package Elastic.Ingest.Elasticsearch
```

## Quick start

**1. Define a document with mapping attributes:**

```csharp
public class Product
{
    [Keyword]
    public string Sku { get; set; }

    [Text]
    public string Name { get; set; }

    [Keyword]
    public string Category { get; set; }
}
```

**2. Declare a mapping context:**

```csharp
[ElasticsearchMappingContext]
[Index<Product>(Name = "products")]
public static partial class MyContext;
```

**3. Create a channel, bootstrap, and write:**

```csharp
var options = new IngestChannelOptions<Product>(transport, MyContext.Product.Context);
using var channel = new IngestChannel<Product>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

foreach (var product in products)
    channel.TryWrite(product);

await channel.WaitForDrainAsync(TimeSpan.FromSeconds(10), ctx);
```

From `[Index<Product>]` the channel infers: target an index named `products`, create component and index templates, use `index` bulk operations, and create a new index on each bootstrap.

## Strategies

When you need more control, use the `IngestStrategies` and `BootstrapStrategies` factory methods:

```csharp
// Data stream with 30-day retention
var strategy = IngestStrategies.DataStream<LogEntry>(context, "30d");
var options = new IngestChannelOptions<LogEntry>(transport, strategy, context);

// Data stream with ILM policy
var strategy = IngestStrategies.DataStream<LogEntry>(context,
    BootstrapStrategies.DataStreamWithIlm("logs-policy", hotMaxAge: "7d", deleteMinAge: "90d"));

// Index with ILM policy
var strategy = IngestStrategies.Index<Product>(context,
    BootstrapStrategies.IndexWithIlm("products-policy"));
```

## Size-aware batching

> Requires **netstandard2.1 / net8.0** or later.

Elasticsearch's coordinating node rejects bulk requests whose body exceeds `http.max_content_length` (default 100 MB) or `indices.breaker.total.limit` (~95% of heap). When event sizes vary widely — a 5 MB document alongside a 2 KB one — a count-only batch can blow past these limits, causing 429 rejections that exhaust retries and drop documents.

Set `OutboundBufferMaxBytes` to cap each request body:

```csharp
var options = new IngestChannelOptions<MyDoc>(transport, context)
{
    BufferOptions = new BufferOptions
    {
        OutboundBufferMaxSize  = 1_000,
        OutboundBufferMaxBytes = 100 * 1024 * 1024, // 100 MB
    }
};
```

Count, age, and byte limits are independent — whichever fires first flushes the batch.

**Each document is serialized exactly once.** When the budget is set, `ExportAsync` serializes each event once to a local buffer, tracks the running byte total, and issues a new `_bulk` HTTP request whenever the total would exceed the limit. A single outbound page may become N sub-requests; their responses are merged transparently before the retry loop sees them. There is no double-serialize, no background measurement pass, and no per-event byte array retained in memory.

**Memory during export:** a local `MemoryStream` accumulates one sub-request body at a time (≤ `OutboundBufferMaxBytes`). It is freed when the export call returns. Peak = `MaxConcurrency × OutboundBufferMaxBytes`.

**Oversized individual events** (a single document larger than the budget) are always emitted in their own sub-request. The `ItemExceedsBytesBudgetCallback` fires as a warning:

```csharp
var options = new IngestChannelOptions<MyDoc>(transport, context)
{
    BufferOptions = new BufferOptions { OutboundBufferMaxBytes = 100 * 1024 * 1024 },
    ItemExceedsBytesBudgetCallback = (doc, bytes) =>
        logger.LogWarning("Document {Id} is {Bytes:N0} bytes, exceeds batch budget", doc.Id, bytes),
};
```

## Helper APIs

Beyond channel-based bulk ingest, the library provides helper APIs for common Elasticsearch operations.
All helpers accept an `ITransport` instance and yield `IAsyncEnumerable` streams for progress monitoring.

- **`PointInTimeSearch<T>`** — iterate all documents in an index using PIT with `search_after` pagination
- **`ServerReindex`** — start a server-side `_reindex` and poll until completion
- **`DeleteByQuery`** — start a `_delete_by_query` and poll until completion
- **`ClientReindex<T>`** — read from a source index via PIT search and write to a destination `IngestChannel`

```csharp
// Example: PIT search
var search = new PointInTimeSearch<MyDoc>(transport, new() { Index = "my-index" });
await foreach (var page in search.SearchPagesAsync())
    Console.WriteLine($"Got {page.Documents.Count} docs");

// Example: Server reindex
var reindex = new ServerReindex(transport, new() { Source = "old", Destination = "new" });
await foreach (var progress in reindex.MonitorAsync())
    Console.WriteLine($"{progress.FractionComplete:P0}");
```

## Documentation

Full documentation: **<https://elastic.github.io/elastic-ingest-dotnet/>**

## Legacy channels

`DataStreamChannel<>` and `IndexChannel<>` still exist for backward compatibility but `IngestChannel<T>` with composable strategies is the recommended API for all new code.
