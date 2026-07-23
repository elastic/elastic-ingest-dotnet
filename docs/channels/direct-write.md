---
navigation_title: Direct write
---

# Direct write

`DirectWriteAsync` writes documents directly to Elasticsearch via the `_bulk` API, bypassing all channel buffering, batching, and retry mechanics. The call is async and returns the `BulkResponse` from Elasticsearch.

## Why

Channels are designed for high-throughput fire-and-forget ingestion: documents are buffered, batched, and exported asynchronously. But some use cases need confirmation that data has been persisted before continuing -- for example, an API handler that must return only after the data is stored.

`DirectWriteAsync` gives you the best of both worlds: use the same channel (with its bootstrap, serialization, and index targeting logic) but make a synchronous request/response call when you need it.

## Usage

`DirectWriteAsync` is available on all Elasticsearch channel types: `IndexChannel`, `DataStreamChannel`, `IngestChannel`, `CatalogIndexChannel`, and `SemanticIndexChannel`.

### Single document

```csharp
var response = await channel.DirectWriteAsync(new Product { Sku = "ABC", Name = "Widget" });

if (response.AllItemsPersisted())
    Console.WriteLine("Document persisted");
```

### Multiple documents

```csharp
var products = new[]
{
    new Product { Sku = "ABC", Name = "Widget" },
    new Product { Sku = "DEF", Name = "Gadget" },
};

var response = await channel.DirectWriteAsync(products);

foreach (var item in response.Items)
    Console.WriteLine($"Status: {item.Status}");
```

### With cancellation

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var response = await channel.DirectWriteAsync(products, cts.Token);
```

### With automatic retries

Failed items with retryable status codes (429, 502, 503, 504) are automatically retried. Only the failed items are re-sent on each retry, not the entire batch.

```csharp
var response = await channel.DirectWriteAsync(
    products,
    retries: 3,
    backoffPeriod: TimeSpan.FromSeconds(5));

if (response.AllItemsPersisted())
    Console.WriteLine("All documents persisted");
```

When `backoffPeriod` is omitted, it defaults to 2 seconds.

## Checking results

The `_bulk` API returns HTTP 200 even when individual items fail. Use `AllItemsPersisted()` to check that every item succeeded:

```csharp
var response = await channel.DirectWriteAsync(product);

if (!response.AllItemsPersisted())
{
    // Inspect individual item failures
    foreach (var item in response.Items.Where(i => i.Status < 200 || i.Status > 299))
        Console.WriteLine($"Failed: {item.Status} - {item.Error?.Reason}");
}
```

`AllItemsPersisted()` returns `true` only when:
- The HTTP request itself succeeded (2xx)
- Every individual bulk item has a 2xx status code

## API reference

| Overload | Description |
|----------|-------------|
| `DirectWriteAsync(IReadOnlyList<TDocument>, CancellationToken)` | Primary overload. Accepts a list of documents and an optional cancellation token. |
| `DirectWriteAsync(params TDocument[])` | Convenience overload for passing documents inline. |
| `DirectWriteAsync(IReadOnlyList<TDocument>, int retries, TimeSpan? backoffPeriod, CancellationToken)` | Retries failed retryable items up to `retries` times with a configurable backoff (default: 2s). |

All overloads return `Task<BulkResponse>`.

| Extension method | Description |
|------------------|-------------|
| `response.AllItemsPersisted()` | Returns `true` if the HTTP request succeeded and every item has a 2xx status. |

## How it works

`DirectWriteAsync` reuses the same internal export path as the buffered channel:

- Same `_bulk` URL (including any index/data stream prefix)
- Same `BulkOperationHeader` per document (create, index, update, etc.)
- Same NDJSON serialization via `BulkRequestDataFactory`
- Same `BulkResponse` deserialization

The only difference is that it calls the transport directly instead of writing to the inbound buffer. There is no batching and no backpressure -- the caller owns the full request lifecycle.

The retry overload uses the same retryable status codes (429, 502, 503, 504) as the buffered channel. On each retry, only the documents whose items had retryable statuses are re-sent. Non-retryable failures (e.g. 400) are dropped immediately.

## Mixing buffered and direct writes

`DirectWriteAsync` and buffered writes (`TryWrite`, `WaitToWriteAsync`) are independent. You can use both on the same channel instance:

```csharp
// Buffered: high-throughput background ingestion
channel.TryWrite(backgroundEvent);

// Direct: API handler needs confirmation
var response = await channel.DirectWriteAsync(apiDocument);
return response.AllItemsPersisted()
    ? Results.Ok()
    : Results.StatusCode(502);
```

## Example: ASP.NET API endpoint

```csharp
app.MapPost("/products", async (Product product, IngestChannel<Product> channel) =>
{
    var response = await channel.DirectWriteAsync(
        new[] { product },
        retries: 2);

    if (!response.AllItemsPersisted())
    {
        if (response.TryGetServerErrorReason(out var reason))
            return Results.Problem(reason);
        return Results.StatusCode(502);
    }

    return Results.Created();
});
```
