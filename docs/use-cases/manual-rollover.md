---
navigation_title: Manual rollover
---

# Manual rollover

Manual rollover gives your application explicit control over when a new backing index is created. Use it when you want to trigger rollover based on application-level events or conditions, without relying on ILM.

## When to use

- You want programmatic control over rollover timing
- ILM is not available or too complex for your use case
- You need to trigger rollover based on application events (deployment, config change, batch completion)

## Document type

```csharp
public class AppEvent
{
    [Timestamp]
    [JsonPropertyName("@timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [Keyword]
    public string EventType { get; set; }

    [Text]
    public string Message { get; set; }

    [Keyword]
    public string Environment { get; set; }
}
```

## Mapping context

```csharp
[ElasticsearchMappingContext]
[DataStream<AppEvent>(
    Type = "logs",
    Dataset = "myapp",
    Namespace = "production"
)]
public static partial class AppEventContext;
```

## Channel setup

To use manual rollover, compose a full `IngestStrategy<T>` that includes `ManualRolloverStrategy`:

```csharp
var tc = AppEventContext.AppEvent.Context;
var strategy = new IngestStrategy<AppEvent>(
    tc,
    BootstrapStrategies.DataStream(),
    new DataStreamIngestStrategy<AppEvent>(
        tc.IndexStrategy?.DataStreamName
            ?? throw new InvalidOperationException("DataStreamName required"),
        "/_bulk"),
    new AlwaysCreateProvisioning(),
    new NoAliasStrategy(),
    new ManualRolloverStrategy()
);
var options = new IngestChannelOptions<AppEvent>(transport, strategy, tc);
using var channel = new IngestChannel<AppEvent>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
```

## Triggering rollover

```csharp
// Condition-based: rollover when index is older than 7 days or larger than 50 GB
await channel.RolloverAsync(maxAge: "7d", maxSize: "50gb");

// Document count: rollover when index exceeds 10 million documents
await channel.RolloverAsync(maxDocs: 10_000_000);

// Unconditional: always create a new backing index
await channel.RolloverAsync();
```

Elasticsearch creates a new backing index only if at least one condition is met (or unconditionally if no conditions are specified).

## Scheduled rollover pattern

```csharp
// Write events continuously
logger.OnEvent += entry => channel.TryWrite(entry);

// Rollover daily via a timer or scheduler
scheduler.Daily += async () =>
{
    await channel.RolloverAsync(maxAge: "1d");
};
```

## Related

- [Rollover API](../index-management/rollover/rollover-api.md): rollover configuration reference
- [Rollover strategies](../strategies/rollover.md): `ManualRolloverStrategy` details
- [Data stream with ILM](data-stream-ilm.md): automatic rollover via ILM policies
