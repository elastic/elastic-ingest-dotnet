# Elastic.Ingest.*

Production-ready bulk ingestion into Elasticsearch — batching, backpressure, retries, and index management handled for you.

Define your document type, declare how it maps to Elasticsearch with source-generated attributes, and get an `IngestChannel<T>` that auto-configures itself from your declaration. Composable strategies let you customize data streams, indices, ILM policies, and lifecycle management. Helper APIs cover PIT search, server-side reindex, delete-by-query, and client-side reindex.

## Documentation

**<https://elastic.github.io/elastic-ingest-dotnet/>**

## Packages

| Package | NuGet | Description |
|---------|-------|-------------|
| [Elastic.Ingest.Elasticsearch](src/Elastic.Ingest.Elasticsearch/README.md) | [![NuGet](https://img.shields.io/nuget/v/Elastic.Ingest.Elasticsearch.svg)](https://www.nuget.org/packages/Elastic.Ingest.Elasticsearch) | `IngestChannel<T>`, composable strategies, bootstrap orchestration, and helper APIs |
| [Elastic.Ingest.Transport](src/Elastic.Ingest.Transport/README.md) | [![NuGet](https://img.shields.io/nuget/v/Elastic.Ingest.Transport.svg)](https://www.nuget.org/packages/Elastic.Ingest.Transport) | Integrates [Elastic.Transport](https://github.com/elastic/elastic-transport-net) HTTP layer with the channel pipeline |
| [Elastic.Channels](src/Elastic.Channels/README.md) | [![NuGet](https://img.shields.io/nuget/v/Elastic.Channels.svg)](https://www.nuget.org/packages/Elastic.Channels) | Thread-safe, batching `ChannelWriter` with backpressure, concurrent export, and retry |

Most users only need to install `Elastic.Ingest.Elasticsearch` — the other packages are pulled in as transitive dependencies.

## Quick start

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

See the [full documentation](https://elastic.github.io/elastic-ingest-dotnet/) for strategies, helpers, index management, and more.
