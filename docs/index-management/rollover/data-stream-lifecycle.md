---
navigation_title: Data stream lifecycle
---

# Data stream lifecycle

Data stream lifecycle (DSL) is a serverless-compatible alternative to ILM. It embeds a retention period directly in the index template, and Elasticsearch handles rollover and deletion automatically.

## Quick setup

Use the `IngestStrategies.DataStream` factory with a retention period:

```csharp
var strategy = IngestStrategies.DataStream<LogEntry>(LoggingContext.LogEntry.Context, "30d");
var options = new IngestChannelOptions<LogEntry>(transport, strategy, LoggingContext.LogEntry.Context);
using var channel = new IngestChannel<LogEntry>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
```

This produces an index template with:

```json
{
    "template": { "lifecycle": { "data_retention": "30d" } },
    "data_stream": {}
}
```

## Explicit step

For manual bootstrap strategy composition, use `BootstrapStrategies.DataStream` with a retention argument:

```csharp
var strategy = IngestStrategies.DataStream<LogEntry>(
    LoggingContext.LogEntry.Context,
    BootstrapStrategies.DataStream("30d"));
var options = new IngestChannelOptions<LogEntry>(transport, strategy, LoggingContext.LogEntry.Context);
```

The `DataStreamLifecycleStep` must execute **before** `DataStreamTemplateStep`, because it sets the retention value that the template step embeds in the template.

## How it works

- `DataStreamLifecycleStep` stores the retention period in `BootstrapContext.Properties`
- `DataStreamTemplateStep` reads it and adds a `lifecycle` block to the template
- Elasticsearch automatically rolls over backing indices and deletes expired data
- No ILM policy is needed

## When to use

- Serverless Elasticsearch (where ILM is not available)
- New projects where you only need retention-based lifecycle
- Any cluster where you want simpler lifecycle management than ILM

## Related

- [ILM managed](ilm-managed.md): multi-phase lifecycle for self-managed clusters
- [ILM and lifecycle](../../advanced/ilm-and-lifecycle.md): detailed comparison
- [Time-series](../../use-cases/time-series.md): using DSL with time-series data
