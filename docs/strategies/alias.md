---
navigation_title: Alias
---

# Alias strategies

Alias strategies manage Elasticsearch aliases after indexing is complete. They enable zero-downtime index swapping and search routing.

## IAliasStrategy

```csharp
public interface IAliasStrategy
{
    Task<bool> ApplyAliasesAsync(AliasContext context, CancellationToken ctx = default);
    bool ApplyAliases(AliasContext context);
}
```

## Built-in strategies

### NoAliasStrategy

No-op. Does not create or modify any aliases. This is the default for data streams and wired streams.

### LatestAndSearchAliasStrategy

Creates two aliases:
1. A **latest alias** pointing to the most recently created index
2. A **search alias** pointing to all indices matching the pattern

This enables a pattern where:
- Writers always target the latest index
- Readers search across all indices via the search alias
- Index rotation is transparent to consumers

Auto-selected when the type context has both a `ReadAlias` (from `SearchStrategy`) and a `WriteTarget` (from `IndexStrategy`).
