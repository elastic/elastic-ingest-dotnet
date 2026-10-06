---
navigation_title: Quick starts
---

# Quick starts

Pick the shape that matches how your data arrives. Every example assumes an `ITransport`:

```csharp
var transport = new DistributedTransport(new TransportConfiguration(new Uri("http://localhost:9200")));
```

| I have... | Use | Needs a mapping context |
|-----------|-----|-------------------------|
| A live feed of events | [Push with a mapping context](#push-with-a-mapping-context) | yes (templates, mappings and bootstrap come from it) |
| A live feed, no mapping | [Push to a plain index](#push-to-a-plain-index) | no |
| A list or `IAsyncEnumerable` to store once | [Store a list](#store-a-list) | no |
| Batches I build, with my own retry and ordering | [Send a batch](#send-a-batch) | no |
| A request handler that must wait until data is stored | [Write and wait](#write-and-wait) | no (works with either channel) |

## Push with a mapping context

Declare the mapping once, the channel bootstraps templates and writes documents in the background.

```csharp
[ElasticsearchMappingContext]
[Index<Product>(Name = "products")]
public static partial class MyContext;

var options = new IngestChannelOptions<Product>(transport, MyContext.Product.Context);
using var channel = new IngestChannel<Product>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
channel.TryWrite(new Product { Sku = "ABC", Name = "Widget" });
await channel.WaitForDrainAsync(TimeSpan.FromSeconds(10), ctx);
```

## Push to a plain index

No attributes, no bootstrap: an index name (or a format with a timestamp) and a transport.

```csharp
var options = new IndexChannelOptions<LogLine>(transport)
{
    IndexFormat = "logs-{0:yyyy.MM.dd}",
    BulkOperationIdLookup = static l => l.Id,   // optional: your own _id
};
using var channel = new IndexChannel<LogLine>(options);

channel.TryWrite(new LogLine { Id = "1", Message = "hello" });
await channel.WaitForDrainAsync(TimeSpan.FromSeconds(10), ctx);
```

## Store a list

You already hold the documents. The library batches, sends concurrently, retries transient failures and returns when everything settled. A source generated serializer context keeps it AOT and trim safe.

```csharp
[JsonSerializable(typeof(Product))]
internal sealed partial class MyJson : JsonSerializerContext;

var result = await BulkSender.IngestAllAsync(transport, MyJson.Default.Product, products, target: "products");

foreach (var failure in result.Failures)          // only what never recovered, with its position in the source
    Console.WriteLine($"#{failure.Position}: {failure.Item.Status} {failure.Item.Error?.Reason}");
```

Works the same for an `IAsyncEnumerable<Product>`. Have a configured channel (strategies, callbacks) instead? `await channel.IngestAllAsync(products)` pulls the sequence through it.

## Send a batch

You own batching, retry and ordering (for example a change data capture pipeline). One call is exactly one `_bulk` request and the response lines up with your batch.

```csharp
var sender = new BulkSender<ChangeRecord, JsonElement>(new()
{
    Transport    = transport,
    BodyTypeInfo = MyJson.Default.JsonElement,
    Body         = static c => c.After,
    Action       = static c => c.Op switch
    {
        Op.Insert => BulkAction.Index(c.Key, c.IndexName),
        Op.Update => BulkAction.Update(c.Key, c.IndexName),
        _         => BulkAction.Delete(c.Key, c.IndexName),
    },
    Retry = BulkRetryPolicy.None,                   // the default: one request, you decide what to do on failure
});

BulkResponse response = await sender.SendAsync(batch, ct);
for (var i = 0; i < batch.Count; i++)
    if (response.Items.ElementAt(i).Status is >= 300) { /* batch[i] failed */ }
```

Turn retries on with `BulkRetryPolicy.Default` or tune them (`MaxRetries`, `Backoff`, `IsRetryable`).

## Write and wait

Persist from a request handler and continue only once Elasticsearch confirmed it:

```csharp
var response = await channel.DirectWriteAsync(new[] { product }, retries: 3);
if (!response.AllItemsPersisted()) { /* respond with an error */ }
```

## Next

- [Bulk sender and pull ingestion](../channels/bulk-sender.md): every `BulkAction`, retry policy, ordering and result details
- [Channel configuration](../channels/composable-channel.md): buffers, concurrency, callbacks
- [Mapping context](mapping-context.md): attributes and templates
