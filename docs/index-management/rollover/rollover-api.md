---
navigation_title: Rollover API
---

# Rollover API

The rollover API creates a new index (or data stream backing index) when the current one meets specified conditions. Elastic.Ingest.Elasticsearch wraps this with `ManualRolloverStrategy`.

## Configuration

To use manual rollover, compose a full `IngestStrategy<T>` that includes `ManualRolloverStrategy`:

```csharp
var strategy = new IngestStrategy<LogEntry>(
    LoggingContext.LogEntry.Context,
    BootstrapStrategies.DataStream(),
    new DataStreamIngestStrategy<LogEntry>("logs-myapp-production", "/_bulk"),
    new AlwaysCreateProvisioning(),
    new NoAliasStrategy(),
    new ManualRolloverStrategy()
);
var options = new IngestChannelOptions<LogEntry>(transport, strategy,
    LoggingContext.LogEntry.Context);
using var channel = new IngestChannel<LogEntry>(options);
```

## Triggering rollover

Call `RolloverAsync` with conditions:

```csharp
// Rollover when the index is older than 7 days OR larger than 50 GB
await channel.RolloverAsync(maxAge: "7d", maxSize: "50gb");

// Rollover when document count exceeds 10 million
await channel.RolloverAsync(maxDocs: 10_000_000);

// Unconditional rollover (always creates a new index)
await channel.RolloverAsync();
```

The channel calls `POST {target}/_rollover` with the specified conditions. If no conditions are provided, the rollover is unconditional.

## How it works

`ManualRolloverStrategy` sends a rollover request to the write alias or data stream:

```json
POST products/_rollover
{
    "conditions": {
        "max_age": "7d",
        "max_size": "50gb"
    }
}
```

Elasticsearch creates a new backing index only if at least one condition is met (or unconditionally if no conditions are specified).

## When to use

- You want explicit application-level control over when rollover happens
- You need condition-based rollover but don't want to set up ILM policies
- Works with both indices (via aliases) and data streams

## Related

- [ILM managed](ilm-managed.md): automatic condition-based rollover via ILM
- [Rollover strategy](../../strategies/rollover.md): `ManualRolloverStrategy` implementation
