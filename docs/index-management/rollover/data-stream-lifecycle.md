---
navigation_title: Data stream lifecycle
---

# Data stream lifecycle

Data stream lifecycle (DSL) is a serverless-compatible alternative to ILM. It embeds a retention period directly in the index template, and Elasticsearch handles rollover and deletion automatically.

## Quick setup

The simplest approach -- set the option on the channel:

```csharp
var options = new IngestChannelOptions<LogEntry>(transport, MyContext.LogEntry.Context)
{
    DataStreamLifecycleRetention = "30d"
};
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

For manual bootstrap strategy composition:

```csharp
var options = new IngestChannelOptions<LogEntry>(transport)
{
    BootstrapStrategy = new DefaultBootstrapStrategy(
        new ComponentTemplateStep(),
        new DataStreamLifecycleStep("30d"),
        new DataStreamTemplateStep()
    )
};
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
- [Time-series](../../getting-started/time-series.md): using DSL with time-series data
