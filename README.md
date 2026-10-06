# Elastic.Ingest.*

Production-ready bulk ingestion into Elasticsearch — batching, backpressure, retries, and index management handled for you.

Define your document type, declare how it maps to Elasticsearch with source-generated attributes, and get an `IngestChannel<T>` that auto-configures itself from your declaration. Composable strategies let you customize data streams, indices, ILM policies, and lifecycle management. Helper APIs cover PIT search, server-side reindex, delete-by-query, and client-side reindex.

Batches flush on count, age, **or byte budget** — whichever fires first — so wildly variable document sizes no longer blow past Elasticsearch's `max_coordinating_bytes` limit. Each event is serialized exactly once: the library slices one outbound page into multiple `_bulk` sub-requests at export time, bounded by the byte budget, with no intermediate buffers retained between events.

## Documentation

**<https://elastic.github.io/elastic-ingest-dotnet/>**

## Packages

| Package | NuGet | Description |
|---------|-------|-------------|
| [Elastic.Ingest.Elasticsearch](src/Elastic.Ingest.Elasticsearch/README.md) | [![NuGet](https://img.shields.io/nuget/v/Elastic.Ingest.Elasticsearch.svg)](https://www.nuget.org/packages/Elastic.Ingest.Elasticsearch) | `IngestChannel<T>`, composable strategies, bootstrap orchestration, and helper APIs |
| [Elastic.Ingest.Transport](src/Elastic.Ingest.Transport/README.md) | [![NuGet](https://img.shields.io/nuget/v/Elastic.Ingest.Transport.svg)](https://www.nuget.org/packages/Elastic.Ingest.Transport) | Integrates [Elastic.Transport](https://github.com/elastic/elastic-transport-net) HTTP layer with the channel pipeline |
| [Elastic.Channels](src/Elastic.Channels/README.md) | [![NuGet](https://img.shields.io/nuget/v/Elastic.Channels.svg)](https://www.nuget.org/packages/Elastic.Channels) | Thread-safe, batching `ChannelWriter` with backpressure, concurrent export, and retry |

Most users only need to install `Elastic.Ingest.Elasticsearch` — the other packages are pulled in as transitive dependencies.

## Quick starts

How does your data arrive? Pick a shape, all of them are on [the quick starts page](https://elastic.github.io/elastic-ingest-dotnet/getting-started/quick-starts).

| I have... | Use |
|-----------|-----|
| A live feed of events | `channel.TryWrite(doc)`: a buffered channel that batches, applies backpressure and retries |
| A list or `IAsyncEnumerable` to store once | `BulkSender.IngestAllAsync(...)` or `channel.IngestAllAsync(...)`: pulls, batches, retries, returns when everything settled |
| Batches I build myself (CDC, my own retry and ordering) | `BulkSender.SendAsync(batch)`: exactly one `_bulk` request, response lines up with your batch |
| A handler that must wait until the data is stored | `channel.DirectWriteAsync(docs)` |

The last three need no `Elastic.Mapping` context and are AOT and trim safe with a source generated serializer context.

### Push, with a mapping context

```csharp
// 1. Define a document
public class Product
{
    [Keyword] public string Sku { get; set; }
    [Text]    public string Name { get; set; }
}

// 2. Declare a mapping context
[ElasticsearchMappingContext]
[Index<Product>(Name = "products")]
public static partial class MyContext;

// 3. Create a channel and write
var options = new IngestChannelOptions<Product>(transport, MyContext.Product.Context);
using var channel = new IngestChannel<Product>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
channel.TryWrite(new Product { Sku = "ABC", Name = "Widget" });
await channel.WaitForDrainAsync(TimeSpan.FromSeconds(10), ctx);
```

### Store a list

```csharp
var result = await BulkSender.IngestAllAsync(transport, MyJson.Default.Product, products, target: "products");
foreach (var failure in result.Failures)
    Console.WriteLine($"#{failure.Position}: {failure.Item.Status}");
```

### Send a batch

```csharp
var sender = BulkSender.Create(transport, MyJson.Default.Order,
    action: static o => BulkAction.Index(id: o.Id, index: $"orders-{o.TenantId}"));

BulkResponse response = await sender.SendAsync(batch, ct);   // Items[i] belongs to batch[i]
```

See the [full documentation](https://elastic.github.io/elastic-ingest-dotnet/) for strategies, helpers, index management, and more.
