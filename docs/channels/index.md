---
navigation_title: Channel types
---

# Channel types

Elastic.Ingest.Elasticsearch provides several channel types for different Elasticsearch ingestion patterns. Each channel handles buffering, batching, and bulk API calls automatically.

## Composable channel

`ElasticsearchChannel<T>` is the recommended channel for new projects. It uses pluggable strategies for all behaviors and auto-configures from `ElasticsearchTypeContext` when available.

```csharp
var options = new ElasticsearchChannelOptions<MyDocument>(transport, MyContext.MyDocument);
var channel = new ElasticsearchChannel<MyDocument>(options);
```

[Learn more ->](composable-channel.md)

## Specialized channels

For simpler use cases or when you don't need the full strategy pattern:

| Channel | Use case |
|---------|----------|
| [DataStreamChannel](data-stream-channel.md) | Data streams with automatic naming |
| [IndexChannel](index-channel.md) | Traditional indices with date-based naming |
| [CatalogChannel](catalog-channel.md) | Catalog/entity storage indices |
| [SemanticChannel](semantic-channel.md) | Indices with ELSER inference endpoints |

## Common patterns

All channels share these capabilities:

- **Buffered writes**: `channel.TryWrite(document)` buffers documents and flushes in batches
- **Bootstrap**: `channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure)` creates templates and indices
- **Drain**: `channel.WaitForDrainAsync(TimeSpan.FromSeconds(30), ctx)` waits for all buffered documents to be sent
