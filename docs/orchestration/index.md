---
navigation_title: Orchestration
---

# Orchestration

Orchestrators coordinate multiple channels for complex ingestion workflows. When you need to manage multiple indices, handle schema migrations, or synchronize alias swaps across channels, orchestrators handle the coordination so you don't have to write that logic yourself.

## ChannelOrchestrator

`ChannelOrchestrator` manages multiple `IngestChannel<T>` instances, handling bootstrap and lifecycle for all channels together:

```csharp
var orchestrator = new ChannelOrchestrator(channel1, channel2, channel3);
await orchestrator.BootstrapAllAsync(BootstrapMethod.Failure);
```

Use `ChannelOrchestrator` when you have multiple independent channels that need coordinated bootstrap but write independently.

## IncrementalSyncOrchestrator

`IncrementalSyncOrchestrator<T>` coordinates a primary and secondary channel for incremental sync workflows. It automatically determines whether to use reindex mode (same schema) or multiplex mode (schema change), and handles drain, refresh, alias swapping, and cleanup in a single `CompleteAsync` call.

Internally, the orchestrator uses [helper APIs](../helpers/index.md) (`ServerReindex`, `DeleteByQuery`) for its reindex and cleanup operations, with proper async task monitoring via `ElasticsearchTaskMonitor`.

```csharp
using var orchestrator = new IncrementalSyncOrchestrator<Product>(
    transport,
    primary: MyContext.Product.Context,
    secondary: MyContext.ProductSemantic.Context
);

var strategy = await orchestrator.StartAsync(BootstrapMethod.Failure);
foreach (var product in products)
    orchestrator.TryWrite(product);
await orchestrator.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(30));
```

[Learn more ->](incremental-sync.md)

## Lower-level helpers

Orchestrators use [helper APIs](../helpers/index.md) internally for `_reindex`, `_delete_by_query`, and PIT-based iteration. If you need direct control over a single operation without the full orchestration workflow, use the helpers directly.
