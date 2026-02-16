# Elastic.Ingest.Elasticsearch

## Why this library?

The Elasticsearch `_bulk` API is the fast path for writing documents, but using it well is hard. You need to batch documents efficiently, handle backpressure when the cluster is overloaded, retry transient failures with exponential backoff, manage index templates and component templates, and coordinate lifecycle policies -- all while keeping your application responsive.

`Elastic.Ingest.Elasticsearch` handles all of that. You define your document type, declare how it maps to Elasticsearch, and the library gives you a production-ready ingestion channel that auto-configures itself from your declaration.

## Simplest example

**1. Define a document and its Elasticsearch mapping:**

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
[Entity<Product>]
public static partial class MyContext;
```

**3. Create a channel and write documents:**

```csharp
var transport = new DistributedTransport(
    new TransportConfiguration(new Uri("http://localhost:9200"))
);

var options = new IngestChannelOptions<Product>(transport, MyContext.Product.Context);
using var channel = new IngestChannel<Product>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

foreach (var product in products)
    channel.TryWrite(product);

await channel.WaitForDrainAsync(TimeSpan.FromSeconds(10), ctx);
```

That's it. No strategy configuration, no template JSON, no bulk request assembly. From `[Entity<Product>]`, the channel inferred: target an index named `product`, create component and index templates, use `index` operations for the bulk API, and create a new index on each bootstrap.

## How the pieces connect

```
  Elastic.Mapping                Elastic.Ingest.Elasticsearch          Elasticsearch
  ──────────────                 ────────────────────────────          ──────────────
  Document attributes     →     IngestChannelOptions               →  Component templates
  [Entity<>] declaration          ↓                                    Index templates
                                IngestChannel                       →  _bulk API
                                  ↓
                                Auto-resolved strategy:
                                  • EntityTarget    → Index / DataStream / WiredStream
                                  • Bootstrap       → Templates, ILM, lifecycle
                                  • Ingest          → Bulk operation headers
                                  • Provisioning    → Create or reuse indices
                                  • Alias           → Read/write aliases
```

`Elastic.Mapping` attributes on your document class describe the Elasticsearch field mapping. The `[Entity<>]` attribute on your mapping context declares the target, naming, and optional aliases. The channel reads this context and auto-resolves a complete strategy.

See [mapping context](getting-started/mapping-context.md) for the full reference on how `[Entity<>]` parameters drive strategy selection.

## Common strategies

When you need more control than zero-config provides, use the `IngestStrategies` and `BootstrapStrategies` factory methods:

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

See [strategies](strategies/index.md) for the full list of factory methods and customization options.

## Install

```shell
dotnet add package Elastic.Ingest.Elasticsearch
```

`Elastic.Mapping` is included as a transitive dependency.

| Package | Description |
|---------|-------------|
| `Elastic.Ingest.Elasticsearch` | Channels, strategies, and orchestrators |
| `Elastic.Mapping` | Source-generated mapping contexts (transitive) |
| `Elastic.Channels` | Buffered channel infrastructure (transitive) |
| `Elastic.Ingest.Transport` | Transport extensions (transitive) |

## Documentation

- [Getting started](getting-started/index.md): install, define documents, create your first channel
- [Channels](channels/index.md): buffer tuning, callbacks, serialization
- [Architecture](architecture/index.md): how the two-stage buffered pipeline works
- [Index management](index-management/index.md): indices, data streams, rollover, and lifecycle
- [Strategies](strategies/index.md): composable strategy pattern and factory methods
- [Orchestration](orchestration/index.md): coordinating multiple channels
- [Helpers](helpers/index.md): PIT search, server-side reindex, delete-by-query, client-side reindex
- [Use cases](use-cases/index.md): end-to-end guides for every ingestion pattern
- [Advanced topics](advanced/index.md): ILM, custom strategies, serialization
