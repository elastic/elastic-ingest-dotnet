---
navigation_title: E-commerce
---

# E-commerce use case

This guide covers a common pattern: syncing a product catalog from a source system (database, CMS, PIM) into Elasticsearch for search.

## Why

Products change -- prices update, descriptions are rewritten, items go out of stock. You need a way to push those changes to Elasticsearch as upserts (create if new, update if changed) while keeping your search index consistent and avoiding downtime during reindexes.

## Scenario

- Products change periodically (prices, descriptions, availability)
- Each product has a unique SKU
- Updates should be upserts: create if new, update if changed
- Old products should be cleaned up after a full sync

## Document type

```csharp
public class Product
{
    [Id]
    [Keyword]
    public string Sku { get; set; }

    [Text(Analyzer = "standard")]
    public string Name { get; set; }

    [Text(Analyzer = "standard")]
    public string Description { get; set; }

    [Keyword]
    public string Category { get; set; }

    [Keyword]
    public decimal Price { get; set; }

    [Keyword]
    public bool InStock { get; set; }

    [ContentHash]
    [Keyword]
    public string Hash { get; set; }

    [Timestamp]
    [JsonPropertyName("@timestamp")]
    public DateTimeOffset UpdatedAt { get; set; }
}
```

Key attributes:
- `[Id]`: marks the field used as the Elasticsearch document `_id` (enables upserts)
- `[ContentHash]`: enables hash-based change detection so unchanged documents are skipped
- `[Timestamp]`: used for date-based index naming

## Mapping context

```csharp
[ElasticsearchMappingContext]
[Index<Product>(
    Name = "products",
    WriteAlias = "products",
    ReadAlias = "products-search",
    DatePattern = "yyyy.MM.dd.HHmmss"
)]
public static partial class CatalogContext;
```

The `DatePattern` creates time-stamped index names (for example, `products-2026.02.15.120000`) and auto-derives a search pattern (`products-*`). The `WriteAlias` and `ReadAlias` enable zero-downtime alias swapping.

## Channel setup

```csharp
var transport = new DistributedTransport(
    new TransportConfiguration(new Uri("http://localhost:9200"))
);

var options = new IngestChannelOptions<Product>(transport, CatalogContext.Product.Context);
using var channel = new IngestChannel<Product>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
```

With this configuration, the channel auto-selects:
- `TypeContextIndexIngestStrategy`: uses `[Id]` for upserts (`index` operations instead of `create`)
- `HashBasedReuseProvisioning`: reuses the existing index if the content hash matches (skips reindex when schema is unchanged)
- `LatestAndSearchAliasStrategy`: swaps the `products` alias to the latest index after drain

## Sync loop

```csharp
foreach (var product in await GetProductsFromSource())
    channel.TryWrite(product);

await channel.WaitForDrainAsync(TimeSpan.FromSeconds(30), ctx);
await channel.ApplyAliasesAsync(string.Empty, ctx);
```

## Production considerations

- Set `BufferOptions.InboundBufferMaxSize` higher for large catalogs (millions of products)
- Use `WaitToWriteAsync` instead of `TryWrite` if you want backpressure when the buffer is full
- Schedule syncs during off-peak hours for large full reindexes
- The `[ContentHash]` attribute enables the provisioning strategy to detect unchanged schemas and reuse the existing index

## Next steps

For a complete sync workflow that handles alias swapping and old-index cleanup automatically, see [IncrementalSyncOrchestrator](../orchestration/incremental-sync.md). It wraps the single-channel pattern shown above with coordinated multi-index management, schema change detection, and automatic cleanup.

## Related

- [Provisioning strategies](../strategies/provisioning.md): how hash-based reuse works
- [Alias strategies](../strategies/alias.md): how alias swapping works
- [Incremental sync](../orchestration/incremental-sync.md): full orchestration workflow
