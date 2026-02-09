---
navigation_title: Index channel
---

# IndexChannel

`IndexChannel<TEvent>` writes documents to traditional Elasticsearch indices with optional date-based naming patterns.

## Usage

```csharp
var channel = new IndexChannel<Product>(
    new IndexChannelOptions<Product>(transport)
    {
        IndexFormat = "products-{0:yyyy.MM.dd}",
        TimestampLookup = p => p.UpdatedAt
    }
);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
channel.TryWrite(new Product { Name = "Widget", UpdatedAt = DateTimeOffset.Now });
```

## Index naming

The `IndexFormat` string is formatted with the document's timestamp. Common patterns:

| Pattern | Example index |
|---------|--------------|
| `products-{0:yyyy.MM.dd}` | `products-2025.01.15` |
| `products-{0:yyyy.MM}` | `products-2025.01` |
| `products` | `products` (no date rotation) |

## When to use

Use `IndexChannel` when:
- You need traditional indices (not data streams)
- You want date-based index rotation
- You have documents with an identifiable timestamp field

For more control, use [ElasticsearchChannel](composable-channel.md) with `IndexIngestStrategy`.
