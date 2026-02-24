---
navigation_title: Getting started
---

# Getting started

`Elastic.Ingest.Elasticsearch` gives you a production-ready, buffered ingestion pipeline for Elasticsearch. It batches documents into `_bulk` requests, retries failures, applies backpressure, and auto-configures index templates -- from a simple mapping declaration on your document type.

## Install

```shell
dotnet add package Elastic.Ingest.Elasticsearch
```

This pulls in `Elastic.Mapping` (source-generated mapping contexts) and `Elastic.Channels` (buffered channel infrastructure) as transitive dependencies.

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
}
```

| Attribute | What it does |
|-----------|-------------|
| `[Id]` | Uses this field as the Elasticsearch `_id` (enables upserts) |
| `[Keyword]` | Maps to a keyword field (exact match, aggregations) |
| `[Text]` | Maps to a text field (full-text search) |
| `[Timestamp]` | Marks the timestamp field (required for data streams, used for date-based index naming) |

## Create a mapping context

The mapping context is a source-generated class that tells the channel what to create in Elasticsearch:

```csharp
[ElasticsearchMappingContext]
[Index<Product>(Name = "products")]
public static partial class MyContext;
```

`[Index<Product>(Name = "products")]` targets an index named `products`. The source generator produces `MyContext.Product.Context` -- an `ElasticsearchTypeContext` containing mappings JSON, settings, and accessor delegates.

For data streams, use `[DataStream<T>]`. For serverless wired streams, use `[WiredStream<T>]`. For runtime-parameterized index names, use `NameTemplate` -- see [templated index names](templated-index-names.md).

See [mapping context](mapping-context.md) for the full attribute reference and how each option drives strategy selection.

## Create and use a channel

```csharp
var transport = new DistributedTransport(
    new TransportConfiguration(new Uri("http://localhost:9200"))
);

var options = new IngestChannelOptions<Product>(transport, MyContext.Product.Context);
using var channel = new IngestChannel<Product>(options);

// Create templates and index in Elasticsearch
await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

// Write documents (buffered, batched automatically)
foreach (var product in products)
    channel.TryWrite(product);

// Wait for all buffered documents to be sent
await channel.WaitForDrainAsync(TimeSpan.FromSeconds(30), ctx);
```

## What the channel inferred

From `[Index<Product>]` and the document attributes, the channel auto-resolved:

| Behavior | Resolved to | Why |
|----------|------------|-----|
| Entity target | `EntityTarget.Index` | `[Index<T>]` attribute used |
| Ingest | `TypeContextIndexIngestStrategy` | `[Id]` present: uses `index` operations (upserts) |
| Bootstrap | `ComponentTemplateStep` + `IndexTemplateStep` | Index target needs component and index templates |
| Provisioning | `AlwaysCreateProvisioning` | No `[ContentHash]` on the document |
| Alias | `NoAliasStrategy` | No `WriteAlias`/`ReadAlias` configured |

Different attribute parameters resolve to different strategies. See [mapping context](mapping-context.md) for the full resolution table, or [index management](../index-management/index.md) for more control over indices, data streams, and lifecycle.

## What happens at runtime

1. **Bootstrap** creates component templates (settings + mappings) and an index template in Elasticsearch. If the template already exists with the same content hash, it skips the update.
2. **TryWrite** buffers documents in memory. When the batch reaches 1,000 items or 5 seconds elapse, the channel sends a `_bulk` request.
3. **Drain** waits for all buffered and inflight documents to reach Elasticsearch before your code continues.

## Next steps

- [Mapping context](mapping-context.md): full attribute reference and strategy resolution
- [Templated index names](templated-index-names.md): runtime-parameterized index names
- [Channels](../channels/index.md): buffer tuning, callbacks, and channel lifecycle
- [Use cases](../use-cases/index.md): end-to-end guides for every ingestion pattern
