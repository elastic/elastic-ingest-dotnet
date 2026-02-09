---
navigation_title: Incremental sync
---

# IncrementalSyncOrchestrator

`IncrementalSyncOrchestrator` coordinates a full incremental sync workflow across multiple channels:

1. **Bootstrap**: create templates and infrastructure
2. **Provision**: create or reuse indices based on content hash
3. **Ingest**: write documents through buffered channels
4. **Alias swap**: atomically swap aliases to point to new indices
5. **Cleanup**: remove old indices

## Usage

```csharp
var orchestrator = new IncrementalSyncOrchestrator(
    new[] { productsChannel, categoriesChannel },
    transport
);

// Run the full sync workflow
await orchestrator.RunAsync(async channels =>
{
    foreach (var product in await GetProducts())
        channels[0].TryWrite(product);

    foreach (var category in await GetCategories())
        channels[1].TryWrite(category);
}, ctx);
```

## Workflow

The orchestrator handles each phase in order:

1. **Bootstrap** all channels (templates, component templates)
2. **Provision** indices for each channel (create new or detect reuse)
3. Execute the user-provided ingestion callback
4. **Drain** all channels (wait for buffered documents to flush)
5. **Apply aliases** (swap search aliases to new indices)
6. **Cleanup** old indices that are no longer referenced

## When to use

Use `IncrementalSyncOrchestrator` when:
- You sync multiple entity types in a coordinated workflow
- You need atomic alias swaps after ingestion completes
- You want hash-based index reuse to skip unchanged entity types
- You need automatic cleanup of orphaned indices
