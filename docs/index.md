# Elastic.Ingest.Elasticsearch

A .NET library for reliable, buffered document ingestion into Elasticsearch. It handles batching, concurrent export, retries, and backpressure automatically, so you can focus on your data.

## Choose your path

| I want to... | Guide |
|--------------|-------|
| Sync a product catalog or e-commerce data | [E-commerce use case](getting-started/e-commerce.md) |
| Index reference data with versioned snapshots | [Catalog data use case](getting-started/catalog-data.md) |
| Ingest logs, metrics, or time-series events | [Time-series use case](getting-started/time-series.md) |

## Quick start

```shell
dotnet add package Elastic.Ingest.Elasticsearch
dotnet add package Elastic.Mapping
```

```csharp
var transport = new DistributedTransport(
    new TransportConfiguration(new Uri("http://localhost:9200"))
);

var options = new IngestChannelOptions<MyDocument>(transport, MyContext.MyDocument.Context);
using var channel = new IngestChannel<MyDocument>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

foreach (var doc in documents)
    channel.TryWrite(doc);

await channel.WaitForDrainAsync(TimeSpan.FromSeconds(30), ctx);
```

## Key features

- **Buffered ingestion**: automatic batching with configurable buffer sizes and flush intervals
- **Composable strategies**: plug in custom bootstrap, ingest, provisioning, alias, and rollover behaviors
- **Auto-configuration**: define mappings with `Elastic.Mapping` attributes and let the channel configure itself
- **Data stream lifecycle**: serverless-compatible retention management
- **Hash-based change detection**: skip redundant template updates and reuse unchanged indices
- **Multi-channel orchestration**: coordinate primary and secondary indices with `IncrementalSyncOrchestrator`
- **AOT support**: source-generated serialization contexts for Native AOT

## Packages

| Package | Description |
|---------|-------------|
| `Elastic.Ingest.Elasticsearch` | Elasticsearch channels, strategies, and orchestrators |
| `Elastic.Channels` | Base buffered channel infrastructure (transitive dependency) |
| `Elastic.Ingest.Transport` | Transport extensions for Elasticsearch API calls (transitive dependency) |

You only need to install `Elastic.Ingest.Elasticsearch`. The other packages are pulled in automatically.

## How it works

Documents flow through a [two-stage buffered pipeline](architecture/push-model.md): producers write to an inbound channel, the library batches items and exports them concurrently via the Elasticsearch `_bulk` API, with automatic retry and backpressure.

The [composable strategy pattern](strategies/index.md) lets you control every aspect of the channel's behavior -- from how documents are serialized to how indices are created and aliases are managed -- or let the channel auto-configure from your `ElasticsearchTypeContext`.

## Documentation

- [Getting started](getting-started/index.md): install, define documents, create your first channel
- [Index management](index-management/index.md): strategies for indices, data streams, rollover, and lifecycle
- [Channels](channels/index.md): channel configuration and options
- [Strategies](strategies/index.md): composable strategy pattern
- [Architecture](architecture/index.md): how the buffered pipeline works
- [Orchestration](orchestration/index.md): coordinating multiple channels
- [Advanced topics](advanced/index.md): ILM, custom strategies, serialization
