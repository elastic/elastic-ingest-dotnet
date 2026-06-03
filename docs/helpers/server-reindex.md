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

## Remote reindex

Reindex documents from a remote Elasticsearch cluster using `RemoteSource`. This is GA in Elastic Cloud Serverless — any ECH deployment or Serverless project endpoint is accepted without an allowlist.

### Basic authentication

```csharp
var reindex = new ServerReindex(transport, new ServerReindexOptions
{
    Source = "remote-index",
    Destination = "local-index",
    Remote = new RemoteSource
    {
        Host = "https://my-deployment.es.us-east-1.aws.elastic.cloud:443",
        Username = "reindex_user",
        Password = "secret",
    },
});

await foreach (var progress in reindex.MonitorAsync())
{
    Console.WriteLine($"Remote reindex {progress.FractionComplete:P0}");
}
```

### API key authentication (Elastic Stack)

Use the native `api_key` field, supported by Elasticsearch 8.x+:

```csharp
var reindex = new ServerReindex(transport, new ServerReindexOptions
{
    Source = "remote-index",
    Destination = "local-index",
    Remote = new RemoteSource
    {
        Host = "https://my-deployment.es.us-east-1.aws.elastic.cloud:443",
        ApiKey = "dGVzdEtleQ==",
        SocketTimeout = "2m",
        ConnectTimeout = "30s",
    },
});
```

### Header-based authentication (Serverless)

For Serverless-to-Serverless reindex where the remote expects a raw `Authorization` header, use the `Headers` dictionary:

```csharp
var reindex = new ServerReindex(transport, new ServerReindexOptions
{
    Source = "remote-index",
    Destination = "local-index",
    Remote = new RemoteSource
    {
        Host = "https://my-project.es.us-east-1.aws.elastic.cloud:443",
        Headers = new Dictionary<string, string>
        {
            ["Authorization"] = "ApiKey base64EncodedApiKey=="
        },
    },
});
```

### Semantic text indices

Indices with `semantic_text` fields need two workarounds for remote reindex:

1. **Batch size** -- `semantic_text` documents with dense vector embeddings can be very large (~200+ KB each). The default batch size of 1000 often exceeds the 100 MB on-heap coordinating buffer, causing `es_rejected_execution_exception`. Lower `SourceSize` to stay within limits. See [elastic/elasticsearch#150635](https://github.com/elastic/elasticsearch/issues/150635).

2. **Inference fields** -- the stored `_source` of `semantic_text` documents includes an `_inference_fields` metadata block. On ingest, the destination cluster also tries to add this field, causing a "Duplicate field '_inference_fields'" parse error. Set `ExcludeInferenceFields = true` to strip it from the source. See [elastic/elasticsearch#150634](https://github.com/elastic/elasticsearch/issues/150634).

   **Caveat:** removing `_inference_fields` causes the destination to re-run inference on every document, even when chunk embeddings already exist in `_source`. This is an Elasticsearch-side limitation.

```csharp
var reindex = new ServerReindex(transport, new ServerReindexOptions
{
    Source = "semantic-index",
    Destination = "semantic-index",
    SourceSize = 100,
    ExcludeInferenceFields = true,
    Conflicts = "proceed",
    Remote = new RemoteSource
    {
        Host = "https://source-project.es.us-east-1.aws.elastic.cloud:443",
        Headers = new Dictionary<string, string>
        {
            ["Authorization"] = "ApiKey base64EncodedApiKey=="
        },
        SocketTimeout = "5m",
        ConnectTimeout = "30s",
    },
});

await foreach (var progress in reindex.MonitorAsync())
{
    Console.WriteLine($"Remote reindex {progress.FractionComplete:P0} -- " +
                      $"{progress.Created} created");
}
```

### Tuning batch size

Remote reindex uses an on-heap buffer that defaults to 100 MB. For large documents (especially `semantic_text` with embeddings), lower the batch size:

```csharp
var reindex = new ServerReindex(transport, new ServerReindexOptions
{
    Source = "remote-large-docs",
    Destination = "local-index",
    SourceSize = 10,
    Remote = new RemoteSource
    {
        Host = "https://remote.es.cloud:443",
        Username = "user",
        Password = "pass",
    },
});
```

### RemoteSource reference

| Property | Type | Description |
|----------|------|-------------|
| `Host` | `string` | Remote Elasticsearch endpoint (scheme + host + port). Required. |
| `Username` | `string?` | Username for basic auth. |
| `Password` | `string?` | Password for basic auth. |
| `ApiKey` | `string?` | API key for the remote cluster. Emitted as the native `api_key` field. |
| `Headers` | `Dictionary<string, string>?` | Custom HTTP headers (e.g. `Authorization` for Serverless). |
| `SocketTimeout` | `string?` | Socket read timeout (e.g. `"1m"`). Default: 30s. |
| `ConnectTimeout` | `string?` | Connection timeout (e.g. `"30s"`). Default: 30s. |

## Scripts

Apply a Painless script to modify documents during reindex. Provide the full JSON script object:

```csharp
var options = new ServerReindexOptions
{
    Source = "old-index",
    Destination = "new-index",
    Script = """{"source":"ctx._source.tag = 'migrated'"}""",
};
```

Scripts compose cleanly with `ExcludeInferenceFields` -- the `_source` exclusion runs at fetch time, and the script runs at index time.

## Full body override

For advanced use cases not covered by the structured options, pass the complete request body directly. When `Body` is set, all other structured options are ignored:

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
| `Slices` | `string?` | `null` | `"auto"` or a number string. Not supported for remote reindex. |
| `SourceSize` | `int?` | `null` | Docs per batch from source (default 1000). Lower for remote with large docs. |
| `MaxDocs` | `long?` | `null` | Maximum total documents to reindex. |
| `Conflicts` | `string?` | `null` | `"abort"` (default) or `"proceed"` to continue on version conflicts. |
| `Script` | `string?` | `null` | Painless script JSON object to modify documents during reindex. |
| `ExcludeInferenceFields` | `bool` | `false` | Strip `_inference_fields` from `_source` (workaround for [#150634](https://github.com/elastic/elasticsearch/issues/150634)). |
| `PollInterval` | `TimeSpan` | `5s` | How often to poll the task status. |
| `Remote` | `RemoteSource?` | `null` | Remote cluster configuration for cross-cluster reindex. |
| `Body` | `string?` | `null` | Full override body JSON. |

## ReindexProgress

Each yielded progress snapshot exposes:

| Property | Type | Description |
|----------|------|-------------|
| `TaskId` | `string` | The Elasticsearch task ID. Stable across relocations when using the reindex management API. |
| `IsCompleted` | `bool` | Whether the task has finished. |
| `Cancelled` | `bool` | Whether the task has been cancelled. |
| `Total` | `long` | Total documents to process. |
| `Created` | `long` | Documents created. |
| `Updated` | `long` | Documents updated. |
| `Deleted` | `long` | Documents deleted. |
| `Noops` | `long` | Documents that were no-ops. |
| `VersionConflicts` | `long` | Version conflicts encountered. |
| `Elapsed` | `TimeSpan` | Time elapsed since the task started. |
| `FractionComplete` | `double?` | 0.0 to 1.0, or `null` if total is unknown. |
| `Description` | `string?` | Sanitized operation description (source/dest, remote host). Only from reindex management API. |
| `StartTime` | `DateTimeOffset?` | When the task started. Only from reindex management API. |
| `Error` | `string?` | Error description if the task failed. |

## Management APIs

`ReindexOperations` exposes the reindex-specific management endpoints introduced in Elasticsearch 9.5.0. These are relocation-aware and work in Serverless (where `/_tasks` is unavailable).

```csharp
var ops = new ReindexOperations(transport);
```

### List running reindex operations

```csharp
var response = await ops.ListAsync(detailed: true);
```

### Get status of a specific reindex

Falls back to `/_tasks/{taskId}` on older clusters.

```csharp
var progress = await ops.GetStatusAsync("r1A2WoRbTwKZ516z6NEs5A:36619");
Console.WriteLine($"{progress.FractionComplete:P0} complete");
```

### Cancel a reindex

Falls back to `/_tasks/{taskId}/_cancel` on older clusters.

```csharp
var finalState = await ops.CancelAsync("r1A2WoRbTwKZ516z6NEs5A:36619");
Console.WriteLine($"Cancelled: {finalState?.Cancelled}");
```

### Rethrottle

```csharp
await ops.RethrottleAsync("r1A2WoRbTwKZ516z6NEs5A:36619", requestsPerSecond: 500);
```

## Related

- [Client-side reindex](client-reindex.md): reindex with application-level document transforms
- [Delete by query](delete-by-query.md): uses the same async task polling pattern
- [Incremental sync](../orchestration/incremental-sync.md): uses server-side reindex internally
