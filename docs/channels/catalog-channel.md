---
navigation_title: Catalog channel
---

# CatalogChannel

`CatalogChannel<TEvent>` is designed for entity storage patterns where documents are upserted by ID into a single index (or alias). It uses `index` operations with explicit document IDs.

## Usage

```csharp
var options = new ElasticsearchChannelOptions<Product>(transport, MyContext.Product);
options.IngestStrategy = new CatalogIngestStrategy<Product>(
    MyContext.Product,
    "products",
    options.BulkPathAndQuery
);

var channel = new ElasticsearchChannel<Product>(options);
```

## When to use

Use a catalog pattern when:
- Documents have stable IDs and are updated in place
- You need point-in-time entity storage (products, users, configuration)
- You want alias-based index swapping for zero-downtime reindexing
