---
navigation_title: Helpers
---

# Helper APIs

Beyond channel-based bulk ingest, the library provides helper APIs for common Elasticsearch operations that work directly with `ITransport`. These are lower-level than [orchestrators](../orchestration/index.md) but higher-level than raw HTTP calls -- they handle pagination, async task polling, and resource cleanup so you don't have to.

All helpers yield `IAsyncEnumerable` streams, giving you progress updates as work proceeds rather than blocking until completion.

## When to use helpers vs orchestrators

Use [orchestrators](../orchestration/index.md) when you need coordinated multi-channel workflows with alias management and strategy resolution. Use helpers when you need direct control over a single Elasticsearch operation.

| Helper | What it does |
|--------|-------------|
| [Point-in-time search](point-in-time-search.md) | Iterates all documents in an index using PIT with `search_after` pagination and optional parallel slicing |
| [Server-side reindex](server-reindex.md) | Triggers `_reindex` with `wait_for_completion=false` and monitors the task |
| [Delete by query](delete-by-query.md) | Triggers `_delete_by_query` with `wait_for_completion=false` and monitors the task |
| [Client-side reindex](client-reindex.md) | Reads via PIT search, writes via `IngestChannel`, with optional per-document transforms |

## Type context integration

All helpers support automatic index resolution via `ElasticsearchTypeContext`. Instead of specifying index names as strings, provide a type context and the helper resolves the correct target:

- **Search helpers** use `ReadAlias` (falling back to `WriteAlias`)
- **Write helpers** use `WriteAlias`

This keeps index names consistent with channel configuration and eliminates string duplication.

## Transport

All helpers accept an `ITransport` instance -- the same one you pass to channel options:

```csharp
var transport = new DistributedTransport(
    new TransportConfiguration(new Uri("http://localhost:9200"))
);
```

## Related

- [Orchestration](../orchestration/index.md): higher-level coordination using `IncrementalSyncOrchestrator`
- [Transport layer](../architecture/transport-layer.md): how `ITransport` is configured
- [Serialization](../advanced/serialization.md): AOT-compatible serialization for document types
