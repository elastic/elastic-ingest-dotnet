---
navigation_title: Client-side reindex
---

# Client-side reindex

`ClientReindex<TDocument>` orchestrates a client-side reindex by reading from a source index using [`PointInTimeSearch`](point-in-time-search.md) and writing to a destination using an `IngestChannel<T>`. This enables applying arbitrary transformations to documents in flight -- something the server-side `_reindex` API cannot do with complex application logic.

## When to use

Use client-side reindex when you need to transform documents between indices using C# code. For simple copies, field renames, or painless-scriptable changes, prefer [server-side reindex](server-reindex.md) which runs entirely on the cluster and is faster.

## Basic usage

```csharp
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.Reindex;
using Elastic.Ingest.Elasticsearch.Search;

// Create and configure the destination channel (caller owns its lifecycle)
var channelOpts = new IngestChannelOptions<MyDocument>(transport, myTypeContext);
using var channel = new IngestChannel<MyDocument>(channelOpts);
await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

var reindex = new ClientReindex<MyDocument>(new ClientReindexOptions<MyDocument>
{
    Source = new PointInTimeSearchOptions
    {
        Index = "source-index",
        Size = 1000,
    },
    Destination = channel,
});

await foreach (var progress in reindex.RunAsync())
{
    Console.WriteLine($"Read: {progress.DocumentsRead}, Written: {progress.DocumentsWritten}");
}
```

## Transforming documents

Pass a `Transform` function to modify documents before they are written. When `Transform` is `null` (the default), documents are written as-is.

```csharp
var options = new ClientReindexOptions<MyDocument>
{
    Source = new PointInTimeSearchOptions { Index = "source-index" },
    Destination = channel,
    Transform = doc =>
    {
        doc.Title = doc.Title.ToUpperInvariant();
        doc.MigratedAt = DateTimeOffset.UtcNow;
        return doc;
    },
};
```

## Filtering the source

Use `QueryBody` on the source options to limit which documents are reindexed:

```csharp
Source = new PointInTimeSearchOptions
{
    Index = "source-index",
    QueryBody = """{"term":{"status":"active"}}""",
},
```

## Type context integration

Instead of specifying index names as strings, you can use an `ElasticsearchTypeContext` on the source options. The index is resolved automatically from the type context's `ReadAlias` (if available) or `WriteAlias`:

```csharp
var reindex = new ClientReindex<MyDocument>(new ClientReindexOptions<MyDocument>
{
    Source = new PointInTimeSearchOptions { TypeContext = myTypeContext },
    Destination = channel,
});
```

## How it works

1. Opens a PIT on the source index and iterates all matching documents using `search_after` pagination
2. For each page, applies the `Transform` function (if set) and writes the batch to the destination channel via `WaitToWriteManyAsync`
3. After all pages are read, calls `WaitForDrainAsync` to flush remaining writes
4. Disposes the PIT in a `finally` block. The caller owns the channel lifecycle.

Serialization options are extracted from the destination channel's configuration automatically, so there's no need to pass them separately.

## Options reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Source` | `PointInTimeSearchOptions` | *required* | PIT search options for reading. See [Point-in-time search](point-in-time-search.md). |
| `Destination` | `IngestChannel<TDocument>` | *required* | The destination channel. Caller owns its lifecycle. |
| `Transform` | `Func<TDocument, TDocument>?` | `null` | Optional per-document transform. |

## ClientReindexProgress

Each yielded progress snapshot exposes:

| Property | Type | Description |
|----------|------|-------------|
| `DocumentsRead` | `long` | Documents read from the source so far. |
| `DocumentsWritten` | `long` | Documents written to the destination so far. |
| `IsCompleted` | `bool` | Whether the reindex has completed. |
| `Elapsed` | `TimeSpan` | Time elapsed since the operation started. |

## Related

- [Point-in-time search](point-in-time-search.md): the read side of client-side reindex
- [Server-side reindex](server-reindex.md): faster alternative when no application-level transform is needed
- [Channels](../channels/index.md): how `IngestChannel` handles batching and retries
