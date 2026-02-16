---
navigation_title: Point-in-time search
---

# Point-in-time search

`PointInTimeSearch<TDocument>` iterates over all documents in an Elasticsearch index using a [point-in-time](https://www.elastic.co/guide/en/elasticsearch/reference/current/point-in-time-api.html) with `search_after` pagination. It returns pages of strongly-typed documents as an `IAsyncEnumerable` and implements `IAsyncDisposable` to close the PIT automatically.

## Why PIT-based iteration?

A PIT captures a consistent snapshot of the index at a point in time. Combined with `search_after`, this gives you a stable cursor that isn't affected by concurrent writes -- unlike scroll, PIT searches are stateless on the search side and don't hold resources on the cluster between requests.

## Basic usage

```csharp
using Elastic.Ingest.Elasticsearch.Search;

var options = new PointInTimeSearchOptions
{
    Index = "my-index",
    Size = 500,
    KeepAlive = "5m",
};

await using var search = new PointInTimeSearch<MyDocument>(transport, options);

await foreach (var page in search.SearchPagesAsync())
{
    Console.WriteLine($"Got {page.Documents.Count} docs (total: {page.TotalDocuments})");
    foreach (var doc in page.Documents)
    {
        // process doc
    }
}
```

Or flatten pages into individual documents:

```csharp
await foreach (var doc in search.SearchDocumentsAsync())
{
    // process each document
}
```

## Filtering

Pass any Elasticsearch query DSL as a JSON string to limit which documents are returned:

```csharp
var options = new PointInTimeSearchOptions
{
    Index = "my-index",
    QueryBody = """{"range":{"@timestamp":{"gte":"now-7d"}}}""",
};
```

When `QueryBody` is `null` (the default), all documents are returned.

## Type context integration

Instead of specifying an index name as a string, you can provide an `ElasticsearchTypeContext`. The index is resolved from `ReadAlias` (if available) or falls back to `WriteAlias`:

```csharp
var options = new PointInTimeSearchOptions
{
    TypeContext = myTypeContext,
    Size = 500,
};
```

You must provide either `Index` or `TypeContext` (or both, in which case `Index` takes precedence).

## Slicing

PIT search supports [sliced scrolling](https://www.elastic.co/guide/en/elasticsearch/reference/current/paginate-search-results.html#slice-scroll) to parallelize reads across shards. All slices share a single PIT and their pages are merged into a single `IAsyncEnumerable` stream.

| `Slices` value | Behaviour |
|----------------|-----------|
| `null` (default) | Auto-detect: uses the index shard count. Serverless instances always use 1. |
| `0` or `1` | No slicing -- single search loop. |
| `> 1` | Runs that many parallel slice queries. |

```csharp
var options = new PointInTimeSearchOptions
{
    Index = "my-index",
    Slices = 4,
};
```

Auto-detection queries the index settings for `number_of_shards` and checks whether the cluster is serverless. If either check fails, it falls back to a single slice.

## Custom serialization

For AOT scenarios on net8.0+, provide a `JsonSerializerOptions` with an appropriate `JsonSerializerContext`:

```csharp
var serializerOptions = new JsonSerializerOptions
{
    TypeInfoResolver = MyJsonContext.Default
};
var search = new PointInTimeSearch<MyDocument>(transport, options, serializerOptions);
```

On netstandard2.0/2.1, reflection-based deserialization is used by default. See [Serialization](../advanced/serialization.md) for more on AOT support.

## Options reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Index` | `string?` | `null` | The index to search. Either this or `TypeContext` must be set. |
| `TypeContext` | `ElasticsearchTypeContext?` | `null` | Auto-resolves the index from ReadAlias or WriteAlias. |
| `QueryBody` | `string?` | `null` | JSON query clause. When `null`, matches all documents. |
| `Sort` | `string?` | `null` | JSON sort expression. Defaults to `"_shard_doc"` for optimal PIT performance. |
| `Size` | `int` | `1000` | Number of documents per page. |
| `KeepAlive` | `string` | `"5m"` | How long the PIT is kept alive between requests. |
| `Slices` | `int?` | `null` | Number of parallel slices. See [Slicing](#slicing). |

## SearchPage&lt;TDocument&gt;

Each yielded page exposes:

| Property | Type | Description |
|----------|------|-------------|
| `Documents` | `IReadOnlyList<TDocument>` | The documents in this page. |
| `TotalDocuments` | `long` | Total matching documents (from `hits.total.value`). |
| `HasMore` | `bool` | Whether more pages remain. |

## Lifecycle

The PIT is opened on the first call to `SearchPagesAsync` or `SearchDocumentsAsync` and closed when the instance is disposed. Always use `await using` or call `DisposeAsync` explicitly to ensure cleanup. If disposal is skipped, the PIT expires on its own after the `KeepAlive` duration.

## Related

- [Client-side reindex](client-reindex.md): uses PIT search to read documents for reindexing
- [Serialization](../advanced/serialization.md): AOT-compatible serialization
