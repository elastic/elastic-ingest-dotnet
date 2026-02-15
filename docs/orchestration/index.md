---
navigation_title: Orchestration
---

# Orchestration

Orchestrators coordinate multiple channels for complex ingestion workflows where you need to manage indices, aliases, and lifecycle across entity types.

## ChannelOrchestrator

`ChannelOrchestrator` manages multiple `IngestChannel<T>` instances, handling bootstrap and lifecycle for all channels together:

```csharp
var orchestrator = new ChannelOrchestrator(channel1, channel2, channel3);
await orchestrator.BootstrapAllAsync(BootstrapMethod.Failure);
```

Use `ChannelOrchestrator` when you have multiple independent channels that need coordinated bootstrap but write independently.

## IncrementalSyncOrchestrator

`IncrementalSyncOrchestrator<T>` coordinates a primary and secondary channel for incremental sync workflows. It automatically determines whether to use reindex mode (same schema) or multiplex mode (schema change), and handles drain, refresh, alias swapping, and cleanup in a single `CompleteAsync` call.

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
