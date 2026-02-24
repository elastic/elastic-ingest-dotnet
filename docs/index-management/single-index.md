---
navigation_title: Single index
---

# Single index

A single, fixed index is the simplest index management strategy. Use it when your data fits in one index and you don't need rollover, aliases, or date-based naming.

## When to use

- Small to medium datasets that don't grow unboundedly
- Reference data that's replaced in full on each sync
- Development and prototyping

## Configuration

Use `[Index<T>]` with a fixed `Name` and no `DatePattern`:

```csharp
[ElasticsearchMappingContext]
[Index<MyDocument>(Name = "my-index")]
public static partial class MyContext;
```

## Channel setup

```csharp
var options = new IngestChannelOptions<MyDocument>(transport, MyContext.MyDocument.Context);
using var channel = new IngestChannel<MyDocument>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
```

Bootstrap creates:
- `my-index-settings` component template
- `my-index-mappings` component template
- `my-index` index template matching `my-index-*`

## Complete example

```csharp
var transport = new DistributedTransport(
    new TransportConfiguration(new Uri("http://localhost:9200"))
);

var options = new IngestChannelOptions<MyDocument>(transport, MyContext.MyDocument.Context);
using var channel = new IngestChannel<MyDocument>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

foreach (var doc in documents)
    channel.TryWrite(doc);

await channel.WaitForDrainAsync(TimeSpan.FromSeconds(30), ctx);
```

## Limitations

- No automatic rollover or rotation
- No alias-based zero-downtime switching
- Index grows indefinitely unless you manage cleanup externally

For growing datasets, consider [data streams](data-streams.md) (append-only) or [rollover strategies](rollover/index.md) (with aliases).
