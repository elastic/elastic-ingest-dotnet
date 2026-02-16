---
navigation_title: Server-side reindex
---

# Server-side reindex

`ServerReindex` triggers an Elasticsearch [`_reindex`](https://www.elastic.co/guide/en/elasticsearch/reference/current/docs-reindex.html) with `wait_for_completion=false` and polls the task API until completion, yielding progress snapshots as an `IAsyncEnumerable<ReindexProgress>`.

## When to use

Use server-side reindex when you want Elasticsearch to copy documents between indices entirely on the server. This is the fastest option when you don't need to transform documents in application code. For transformations beyond what ingest pipelines or painless scripts support, see [client-side reindex](client-reindex.md).

The `IncrementalSyncOrchestrator` uses server-side reindex internally for its reindex-mode workflow. See [incremental sync](../orchestration/incremental-sync.md).

## Basic usage

```csharp
using Elastic.Ingest.Elasticsearch.Helpers;

var reindex = new ServerReindex(transport, new ServerReindexOptions
{
    Source = "old-index",
    Destination = "new-index",
});

await foreach (var progress in reindex.MonitorAsync())
{
    Console.WriteLine($"Reindex {progress.FractionComplete:P0} -- " +
                      $"{progress.Created} created, {progress.Updated} updated");
}
```

Or run to completion in a single call:

```csharp
var result = await reindex.RunAsync();
Console.WriteLine($"Done: {result.Created} created, {result.Updated} updated");
```

## Type context integration

Instead of specifying index names as strings, you can provide `ElasticsearchTypeContext` instances. The source and destination indices are resolved from the type context's `WriteAlias`:

```csharp
var reindex = new ServerReindex(transport, new ServerReindexOptions
{
    SourceContext = sourceTypeContext,
    DestinationContext = destTypeContext,
});
```

You can mix explicit strings and type contexts -- for example, providing `Source` as a string and `DestinationContext` as a type context. When `Body` is set, source/destination resolution is skipped.

## Filtering

Pass a JSON query to reindex a subset of documents:

```csharp
var options = new ServerReindexOptions
{
    Source = "old-index",
    Destination = "new-index",
    Query = """{"range":{"@timestamp":{"gte":"now-7d"}}}""",
};
```

## Ingest pipeline

Apply an ingest pipeline during reindex:

```csharp
var options = new ServerReindexOptions
{
    Source = "old-index",
    Destination = "new-index",
    Pipeline = "my-enrich-pipeline",
};
```

## Throttling and slicing

```csharp
var options = new ServerReindexOptions
{
    Source = "old-index",
    Destination = "new-index",
    RequestsPerSecond = 1000,
    Slices = "auto",
};
```

## Full body override

For advanced use cases not covered by the structured options (scripts, remote reindex, custom conflict handling), pass the complete request body directly. When `Body` is set, `Source`, `Destination`, `Query`, and `Pipeline` are ignored:

```csharp
var options = new ServerReindexOptions
{
    Body = """
    {
      "source": { "index": "old-index" },
      "dest": { "index": "new-index" },
      "script": { "source": "ctx._source.tag = 'migrated'" }
    }
    """,
};
```

## Options reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Source` | `string?` | `null` | Source index name. Either this or `SourceContext` required (unless `Body` is set). |
| `Destination` | `string?` | `null` | Destination index name. Either this or `DestinationContext` required (unless `Body` is set). |
| `SourceContext` | `ElasticsearchTypeContext?` | `null` | Auto-resolves source from WriteAlias. |
| `DestinationContext` | `ElasticsearchTypeContext?` | `null` | Auto-resolves destination from WriteAlias. |
| `Query` | `string?` | `null` | JSON query body to filter source documents. |
| `Pipeline` | `string?` | `null` | Ingest pipeline name. |
| `RequestsPerSecond` | `float?` | `null` | Throttle. Use `-1` for unlimited. |
| `Slices` | `string?` | `null` | `"auto"` or a number string. |
| `PollInterval` | `TimeSpan` | `5s` | How often to poll the task status. |
| `Body` | `string?` | `null` | Full override body JSON. |

## ReindexProgress

Each yielded progress snapshot exposes:

| Property | Type | Description |
|----------|------|-------------|
| `TaskId` | `string` | The Elasticsearch task ID. |
| `IsCompleted` | `bool` | Whether the task has finished. |
| `Total` | `long` | Total documents to process. |
| `Created` | `long` | Documents created. |
| `Updated` | `long` | Documents updated. |
| `Deleted` | `long` | Documents deleted. |
| `Noops` | `long` | Documents that were no-ops. |
| `VersionConflicts` | `long` | Version conflicts encountered. |
| `Elapsed` | `TimeSpan` | Time elapsed since the task started. |
| `FractionComplete` | `double?` | 0.0 to 1.0, or `null` if total is unknown. |
| `Error` | `string?` | Error description if the task failed. |

## Related

- [Client-side reindex](client-reindex.md): reindex with application-level document transforms
- [Delete by query](delete-by-query.md): uses the same async task polling pattern
- [Incremental sync](../orchestration/incremental-sync.md): uses server-side reindex internally
