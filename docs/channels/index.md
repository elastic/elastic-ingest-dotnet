---
navigation_title: Channels
---

# Channels

`IngestChannel<T>` is the primary channel type for ingesting documents into Elasticsearch. It uses a [composable strategy pattern](../strategies/index.md) for all behaviors and auto-configures from `ElasticsearchTypeContext` when available.

## Quick example

```csharp
var options = new IngestChannelOptions<Product>(transport, MyContext.Product.Context);
using var channel = new IngestChannel<Product>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

channel.TryWrite(new Product { Sku = "ABC", Name = "Widget" });
await channel.WaitForDrainAsync(TimeSpan.FromSeconds(30), ctx);
```

## Topics

- [Channel configuration](composable-channel.md): options, buffer configuration, strategies, callbacks
- [Legacy channels](legacy-channels.md): migration guide for `DataStreamChannel`, `IndexChannel`, `CatalogChannel`, and semantic channels

## Channel lifecycle

1. **Create**: instantiate with options (and optional `ElasticsearchTypeContext` for auto-configuration)
2. **Bootstrap**: call `BootstrapElasticsearchAsync` to create templates and indices
3. **Write**: use `TryWrite` (non-blocking) or `WaitToWriteAsync` (with backpressure) to buffer documents
4. **Drain**: call `WaitForDrainAsync` to flush all buffered documents
5. **Alias** (optional): call `ApplyAliasesAsync` to swap aliases after indexing
6. **Rollover** (optional): call `RolloverAsync` to trigger manual index rollover
7. **Dispose**: the channel implements `IDisposable`

## Writing documents

| Method | Behavior |
|--------|----------|
| `TryWrite(doc)` | Non-blocking. Returns `false` if buffer is full. |
| `WaitToWriteAsync(doc, ctx)` | Async with backpressure. Blocks if buffer is full. |
| `TryWriteMany(docs)` | Non-blocking batch write. |
| `WaitToWriteManyAsync(docs, ctx)` | Async batch write with backpressure. |

See [push model](../architecture/push-model.md) for details on buffering, batching, and concurrent export.
