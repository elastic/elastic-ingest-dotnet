---
navigation_title: Getting started
---

# Getting started

This guide walks you through installing Elastic.Ingest.Elasticsearch, defining a document type, and writing your first documents.

## Install

```shell
dotnet add package Elastic.Ingest.Elasticsearch
```

You also need `Elastic.Mapping` for source-generated type contexts:

```shell
dotnet add package Elastic.Mapping
```

## Define a document type

Use `Elastic.Mapping` attributes to describe your document's Elasticsearch mapping:

```csharp
public class Product
{
    [Id]
    [Keyword]
    public string Sku { get; set; }

    [Text(Analyzer = "standard")]
    public string Name { get; set; }

    [Keyword]
    public string Category { get; set; }

    [Timestamp]
    [JsonPropertyName("@timestamp")]
    public DateTimeOffset UpdatedAt { get; set; }
}
```

## Register in a mapping context

Create a source-generated mapping context that tells the channel how to configure itself:

```csharp
[ElasticsearchMappingContext]
[Entity<Product>(
    Target = EntityTarget.Index,
    Name = "products"
)]
public static partial class MyMappingContext;
```

The source generator produces `MyMappingContext.Product` with an `ElasticsearchTypeContext` containing mappings JSON, settings, and accessor delegates.

## Create and use a channel

```csharp
var transport = new DistributedTransport(
    new TransportConfiguration(new Uri("http://localhost:9200"))
);

var options = new IngestChannelOptions<Product>(transport, MyMappingContext.Product.Context);
using var channel = new IngestChannel<Product>(options);

// Create templates and index
await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

// Write documents
foreach (var product in products)
    channel.TryWrite(product);

// Wait for all buffered documents to be sent
await channel.WaitForDrainAsync(TimeSpan.FromSeconds(30), ctx);
```

## What happens

1. **Bootstrap** creates component templates (settings + mappings) and an index template in Elasticsearch. If the template already exists with the same content hash, it skips the update.
2. **TryWrite** buffers documents in memory. When the batch reaches 1,000 items or 5 seconds elapse, the channel sends a `_bulk` request.
3. **Drain** waits for all buffered and inflight documents to reach Elasticsearch before your code continues.

## Next steps

Choose the guide that matches your use case:

- [E-commerce and product catalogs](e-commerce.md): periodic sync of product data with upserts
- [Catalog and reference data](catalog-data.md): versioned snapshots with dual-index orchestration
- [Time-series data](time-series.md): high-volume append-only logs and metrics

Or explore the library in depth:

- [Index management](../index-management/index.md): strategies for managing indices, data streams, and lifecycle
- [Channels](../channels/index.md): channel configuration and options
- [Strategies](../strategies/index.md): composable strategy pattern
- [Architecture](../architecture/index.md): how the buffered pipeline works
