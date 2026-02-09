---
navigation_title: Orchestration
---

# Orchestration

Orchestrators coordinate multiple channels for complex ingestion workflows.

## ChannelOrchestrator

`ChannelOrchestrator` manages multiple `ElasticsearchChannel<T>` instances, handling bootstrap and lifecycle for all channels together:

```csharp
var orchestrator = new ChannelOrchestrator(channel1, channel2, channel3);
await orchestrator.BootstrapAllAsync(BootstrapMethod.Failure);
```

## IncrementalSyncOrchestrator

For incremental sync patterns where you need coordinated provisioning, ingestion, alias swapping, and cleanup across multiple entity types.

[Learn more ->](incremental-sync.md)
