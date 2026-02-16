---
navigation_title: Delete by query
---

# Delete by query

`DeleteByQuery` triggers an Elasticsearch [`_delete_by_query`](https://www.elastic.co/guide/en/elasticsearch/reference/current/docs-delete-by-query.html) with `wait_for_completion=false` and polls the task API until completion, yielding progress snapshots as an `IAsyncEnumerable<DeleteByQueryProgress>`.

## When to use

Use delete-by-query for bulk deletion of documents matching a query -- cleaning up old data, removing a subset after migration, or pruning stale records. This is the same pattern used by `IncrementalSyncOrchestrator` to clean up old documents after alias swaps. See [incremental sync](../orchestration/incremental-sync.md).

## Basic usage

```csharp
using Elastic.Ingest.Elasticsearch.Helpers;

var dbq = new DeleteByQuery(transport, new DeleteByQueryOptions
{
    Index = "my-index",
    QueryBody = """{"range":{"@timestamp":{"lt":"now-30d"}}}""",
});

await foreach (var progress in dbq.MonitorAsync())
{
    Console.WriteLine($"Deleted {progress.Deleted}/{progress.Total} ({progress.FractionComplete:P0})");
}
```

Or run to completion in a single call:

```csharp
var result = await dbq.RunAsync();
Console.WriteLine($"Deleted {result.Deleted} documents");
```

## Type context integration

Instead of specifying an index name as a string, you can provide an `ElasticsearchTypeContext`. The index is resolved from the type context's `WriteAlias`:

```csharp
var dbq = new DeleteByQuery(transport, new DeleteByQueryOptions
{
    TypeContext = myTypeContext,
    QueryBody = """{"range":{"@timestamp":{"lt":"now-30d"}}}""",
});
```

You must provide either `Index` or `TypeContext` (or both, in which case `Index` takes precedence).

## Throttling and slicing

Throttle the delete rate or parallelize across slices:

```csharp
var options = new DeleteByQueryOptions
{
    Index = "my-index",
    QueryBody = """{"match_all":{}}""",
    RequestsPerSecond = 500,
    Slices = "auto",
};
```

## Options reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Index` | `string?` | `null` | The target index. Either this or `TypeContext` must be set. |
| `TypeContext` | `ElasticsearchTypeContext?` | `null` | Auto-resolves the index from WriteAlias. |
| `QueryBody` | `string` | *required* | JSON query body. |
| `RequestsPerSecond` | `float?` | `null` | Throttle. Use `-1` for unlimited. |
| `Slices` | `string?` | `null` | `"auto"` or a number string. |
| `PollInterval` | `TimeSpan` | `5s` | How often to poll the task status. |

## DeleteByQueryProgress

Each yielded progress snapshot exposes:

| Property | Type | Description |
|----------|------|-------------|
| `TaskId` | `string` | The Elasticsearch task ID. |
| `IsCompleted` | `bool` | Whether the task has finished. |
| `Deleted` | `long` | Documents deleted so far. |
| `Total` | `long` | Total documents to process. |
| `VersionConflicts` | `long` | Version conflicts encountered. |
| `Elapsed` | `TimeSpan` | Time elapsed since the task started. |
| `FractionComplete` | `double?` | 0.0 to 1.0, or `null` if total is unknown. |
| `Error` | `string?` | Error description if the task failed. |

## Related

- [Server-side reindex](server-reindex.md): uses the same async task polling pattern
- [Incremental sync](../orchestration/incremental-sync.md): uses delete-by-query for cleanup
