---
navigation_title: Manual alias
---

# Manual alias management

Manual alias management gives you full control over when indices rotate. You create time-stamped indices, write to them, and swap aliases after indexing completes. This is the approach used by `IncrementalSyncOrchestrator` and the catalog data pattern.

## How it works

1. A new index is created with a time-stamped name (for example, `products-2026.02.15.120000`)
2. Documents are written to the new index
3. After drain and refresh, the **write alias** (`products`) is swapped to point to the new index
4. The **search alias** (`products-search`) is updated to include the new index
5. Old indices are optionally cleaned up

## Configuration

Configure aliases in the entity declaration:

```csharp
[Entity<Product>(
    Target = EntityTarget.Index,
    Name = "products",
    WriteAlias = "products",
    ReadAlias = "products-search",
    SearchPattern = "products-*",
    DatePattern = "yyyy.MM.dd.HHmmss"
)]
```

| Property | Purpose |
|----------|---------|
| `WriteAlias` | Alias that points to the current write target |
| `ReadAlias` | Alias for search queries (can span multiple indices) |
| `SearchPattern` | Glob pattern matching all indices for this entity |
| `DatePattern` | Suffix pattern for time-stamped index names |

## Auto-configured strategy

When `WriteAlias` and `ReadAlias` are set, the channel automatically uses `LatestAndSearchAliasStrategy`. This strategy:

- Creates (or updates) the write alias to point to the newest index
- Creates (or updates) the search alias to include all matching indices
- Uses `_aliases` API for atomic alias operations

## Applying aliases

After writing and draining, apply aliases explicitly:

```csharp
await channel.WaitForDrainAsync(TimeSpan.FromSeconds(30), ctx);
await channel.ApplyAliasesAsync(string.Empty, ctx);
```

Or use `IncrementalSyncOrchestrator`, which calls `CompleteAsync` to handle drain, refresh, and alias swapping together.

## Related

- [Alias strategy](../../strategies/alias.md): `LatestAndSearchAliasStrategy` implementation details
- [Catalog data](../../getting-started/catalog-data.md): end-to-end example with alias management
- [Incremental sync](../../orchestration/incremental-sync.md): orchestrated alias swapping
