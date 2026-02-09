# Elastic.Ingest.Elasticsearch

A .NET library for reliable, buffered document ingestion into Elasticsearch. Supports data streams, indices, wired streams, and catalog-based ingestion with composable strategies for bootstrap, provisioning, aliasing, and rollover.

## Key features

- **Buffered ingestion**: automatic batching with configurable buffer sizes and flush intervals
- **Composable strategies**: plug in custom bootstrap, ingest, provisioning, alias, and rollover behaviors
- **Auto-configuration**: define your mappings with `Elastic.Mapping` and let the channel auto-configure from `ElasticsearchTypeContext`
- **Data stream lifecycle**: serverless-compatible alternative to ILM with automatic retention configuration
- **Hash-based change detection**: skip redundant template updates when mappings haven't changed
- **Multi-channel orchestration**: coordinate multiple channels with `ChannelOrchestrator` and `IncrementalSyncOrchestrator`

## Install

```shell
dotnet add package Elastic.Ingest.Elasticsearch
```

## Quick start

```csharp
var transport = new DistributedTransport(new TransportConfiguration(new Uri("http://localhost:9200")));
var options = new ElasticsearchChannelOptions<MyDocument>(transport, MyContext.MyDocument);
var channel = new ElasticsearchChannel<MyDocument>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
channel.TryWrite(new MyDocument { Title = "Hello" });
```

[Learn about channel types ->](channels/index.md)

[Learn about strategies ->](strategies/index.md)

## Packages

| Package | Description |
|---------|-------------|
| `Elastic.Ingest.Elasticsearch` | Elasticsearch-specific channels and strategies |
| `Elastic.Channels` | Base buffered channel infrastructure |
| `Elastic.Ingest.Transport` | Transport extensions for Elasticsearch API calls |
