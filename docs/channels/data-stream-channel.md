---
navigation_title: Data stream channel
---

# DataStreamChannel

`DataStreamChannel<TEvent>` is a specialized channel for writing to Elasticsearch data streams. It automatically configures data stream naming, component templates, and index templates.

## Usage

```csharp
var channel = new DataStreamChannel<LogEvent>(
    new DataStreamChannelOptions<LogEvent>(transport)
    {
        DataStream = new DataStreamName("logs", "myapp", "default")
    }
);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
channel.TryWrite(new LogEvent { Message = "Application started" });
```

## Data stream naming

Data streams follow the `{type}-{dataset}-{namespace}` convention:

- **type**: `logs`, `metrics`, or `traces`
- **dataset**: your application-specific dataset name
- **namespace**: environment or tenant identifier (defaults to `default`)

## Bootstrap

The channel creates:
1. A settings component template with ILM policy reference
2. A mappings component template with your document mappings
3. An index template with `"data_stream": {}` and inferred component templates (`logs-settings`, `logs-mappings`, etc.)

## When to use

Use `DataStreamChannel` when:
- You have time-series data (logs, metrics, events)
- You want automatic data stream naming and template management
- You don't need custom strategy composition

For more control, use [ElasticsearchChannel](composable-channel.md) with `DataStreamIngestStrategy`.
