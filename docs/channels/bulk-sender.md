---
navigation_title: Bulk sender and pull ingestion
---

# Bulk sender and pull ingestion

Channels are a *push* model: you write events, the channel owns the threads, batching, concurrency and retry. Two other shapes are common:

| You have | Use |
|----------|-----|
| One batch you built yourself, and your own retry/ordering/threading | [`BulkSender`](#bulksender): one request per call, no threads, nothing to dispose |
| A finite list or `IAsyncEnumerable` you want stored | [`IngestAllAsync`](#ingestallasync): pulls, batches, sends and returns when everything settled |
| A configured channel (strategies, bootstrap, callbacks) and a finite sequence | [`channel.IngestAllAsync`](#pulling-through-a-channel) |

None of these require `Elastic.Mapping`, an `[Index<T>]` attribute or a bootstrap step. All of them work with a plain `ITransport` and a `JsonTypeInfo<T>`, so they are AOT and trim safe when you use a source generated serializer context.

## BulkSender

A `BulkSender<TItem, TBody>` is immutable and thread safe. Per call it serializes into a pooled buffer, sends one `_bulk` request, and returns the typed `BulkResponse`.

```csharp
var sender = BulkSender.Create(transport, MyContext.Default.Product,
    action: static p => BulkAction.Index(id: p.Sku), target: "products");

BulkResponse response = await sender.SendAsync(products, ct);   // POST products/_bulk
if (!response.AllItemsPersisted()) { /* inspect response.Items */ }
```

`SendAsync` accepts a `ReadOnlySpan<TItem>` (arrays, `CollectionsMarshal.AsSpan(list)`, slices) or an `IEnumerable<TItem>`.

### Controlling the action per item

The `Action` delegate runs once per item and returns a `BulkAction`. It is a small struct, so this does not allocate.

| `BulkAction` | Written as |
|--------------|------------|
| `Index(id?, index?)` | `{"index":{...}}` + document |
| `Create(id?, index?)` | `{"create":{...}}` + document (409 if the id exists) |
| `Update(id, index?)` | `{"update":{...}}` + `{"doc_as_upsert":true,"doc":...}` |
| `Delete(id, index?)` | `{"delete":{...}}` only, no body line |
| `ScriptedHashUpsert(id, HashedBulkUpdate, index?)` | scripted upsert that only replaces the document when its hash changed |

`.WithRequireAlias()` and `.WithDynamicTemplates(...)` modify an action. When `index` is `null` the sender's `Target` applies (`{target}/_bulk`); when both are `null` the request goes to `_bulk` and the action must carry the index.

`TItem` and `TBody` are separate so the action can be derived from a change record while the body is just the payload:

```csharp
var sender = new BulkSender<ChangeRecord, JsonElement>(new()
{
    Transport    = transport,
    BodyTypeInfo = MyContext.Default.JsonElement,
    Body         = static c => c.After,              // not called for deletes
    Action       = static c => c.Op switch
    {
        Op.Insert => BulkAction.Index(c.Key, c.IndexName),
        Op.Update => BulkAction.Update(c.Key, c.IndexName),
        Op.Delete => BulkAction.Delete(c.Key, c.IndexName),
        _ => throw new ArgumentOutOfRangeException()
    },
    Retry = BulkRetryPolicy.None                     // the caller owns retry
});
```

### Retry is opt-in

The default is `BulkRetryPolicy.None`: exactly one request. `BulkRetryPolicy.Default` retries items that failed with 429, 502, 503 or 504 up to three times, two seconds apart. Everything is replaceable:

```csharp
Retry = BulkRetryPolicy.Default with
{
    MaxRetries  = 5,
    Backoff     = static n => TimeSpan.FromMilliseconds(100 * (1 << n)),
    IsRetryable = static item => item.Status is 429 or 503
}
```

Only the failed items are re-sent, without serializing them again. **`response.Items` always lines up position by position with the items you passed in**, also after retries: each position holds the last result for that item, so `response.Items[i]` belongs to `items[i]`. A top level HTTP 429 re-sends the whole request (`RetryAllOnHttp429`).

### Refresh and timeout

Two options apply to every request a sender sends, including retries and the batches of `IngestAllAsync`:

```csharp
var sender = BulkSender.Create(transport, MyContext.Default.Order,
    action: static o => BulkAction.Index(id: o.Id),
    target: "orders",
    refresh: BulkRefresh.WaitFor,                       // POST orders/_bulk?refresh=wait_for&filter_path=...
    requestTimeout: TimeSpan.FromSeconds(30));
```

| Option | Effect |
|--------|--------|
| `Refresh` | Sets the `refresh` parameter: `BulkRefresh.WaitFor` returns once the documents are visible to search without forcing a refresh, `True` refreshes the affected shards before returning, `False` sends `refresh=false`. `null` (the default) sends nothing and Elasticsearch refreshes on the index's own interval. |
| `RequestTimeout` | The client side timeout of each `_bulk` request, layered on top of the transport's own configuration. `null` (the default) leaves the transport's timeout in place. It must be positive or `Timeout.InfiniteTimeSpan`. |

`RequestTimeout` is the client's wait for the HTTP response. It is not Elasticsearch's server side `timeout` parameter, which limits how long the cluster waits for active shards or mapping updates.

Both are fixed for the lifetime of a sender. To vary them per call, keep one sender per setting. A sender is cheap, immutable and thread safe. There is deliberately no free form query string, so these options cannot collide with the built in `filter_path`.

### Serialization settings

By default a sender serializes documents the same way channels do: with `DefaultIgnoreCondition = WhenWritingDefault` (members holding their default value, such as `int N = 0` or a `null` string, are omitted). Output from `BulkSender` and from a channel is therefore identical for the same document.

This default is applied on top of your `JsonTypeInfo`: it **overrides a `DefaultIgnoreCondition` set on your serializer context** (via `[JsonSourceGenerationOptions]`), exactly as it does for channels. Per property attributes such as `[JsonIgnore(Condition = ...)]`, custom converters and naming policies keep working. To serialize exactly as your type info is configured, set `ApplyLibrarySerializerDefaults = false` on `BulkSenderOptions`.

## IngestAllAsync

For "I have a list, store it":

```csharp
BulkIngestAllResult result = await BulkSender.IngestAllAsync(
    transport, MyContext.Default.Product, products, target: "products");

if (result.Failures.Count > 0)
    foreach (var f in result.Failures)
        Console.WriteLine($"{f.Position}: {f.Item.Status} {f.Item.Error?.Reason}");
```

It batches the source (`BatchSize`, default 1,000), sends up to `MaxConcurrency` requests at a time (default: processor count, `1` keeps strict order), retries with `BulkRetryPolicy.Default` unless `IngestAllOptions.Retry` says otherwise, and returns when every batch settled. `Failures` are the items that were still failing after retries, with their position in the source. Arrays and lists are sliced without copying; other sequences are copied into pooled arrays. There is an `IAsyncEnumerable<T>` overload, and the same methods exist as instance methods on a configured `BulkSender`.

## Pulling through a channel

When you want a channel's strategies, bootstrap and callbacks but already hold a finite sequence:

```csharp
IngestAllResult result = await channel.IngestAllAsync(products, maxConcurrency: 1, ctx);
```

The channel fills batches of `BatchExportSize` straight from the source and exports them with the same retry and callback machinery used for pushed events. It bypasses the inbound buffer: no threads are started, `InflightEvents` is untouched, the channel is not completed, and the task completes exactly when every batch settled, so no `WaitForDrainAsync` is needed. The channel stays usable: call `IngestAllAsync` repeatedly, or mix it with `TryWrite`.

- `maxConcurrency` defaults to the channel's `MaxConcurrency`; `1` preserves order.
- For `IAsyncEnumerable` sources a partial batch older than `OutboundBufferMaxLifetime` is exported even while the source is stalled.
- `IngestAllResult.RetriesExhausted` counts items that were still failing when `ExportMaxRetries` ran out. Permanently rejected items are reported through `ServerRejectionCallback` as for pushed events.
- This works for a channel built from plain `IndexChannelOptions<T>` (an `IndexFormat` and a transport) as well as for the `[Index<T>]` mapping context path.
