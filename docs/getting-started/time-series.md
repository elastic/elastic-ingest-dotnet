---
navigation_title: Time-series
---

# Time-series use case

This guide covers append-only time-series data: logs, metrics, traces, and similar event streams.

## Scenario

- High-volume, append-only data
- Documents are never updated after writing
- Data has a natural timestamp
- Retention policy controls how long data is kept
- Data streams provide automatic rollover and lifecycle management

## Document type

```csharp
public class LogEntry
{
    [Timestamp]
    [JsonPropertyName("@timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [Keyword]
    public string Level { get; set; }

    [Text(Analyzer = "standard")]
    public string Message { get; set; }

    [Keyword]
    public string Service { get; set; }

    [Keyword]
    public string Host { get; set; }
}
```

For time-series data, you typically don't need `[Id]` (documents are append-only) or `[ContentHash]` (no deduplication needed).

## Mapping context

```csharp
[ElasticsearchMappingContext]
[Entity<LogEntry>(
    Target = EntityTarget.DataStream,
    DataStreamType = "logs",
    DataStreamDataset = "myapp",
    DataStreamNamespace = "production"
)]
public static partial class LoggingContext;
```

This targets the data stream `logs-myapp-production`, following the Elasticsearch naming convention `{type}-{dataset}-{namespace}`.

## Channel setup

```csharp
var transport = new DistributedTransport(
    new TransportConfiguration(new Uri("http://localhost:9200"))
);

var options = new IngestChannelOptions<LogEntry>(transport, LoggingContext.LogEntry.Context)
{
    DataStreamLifecycleRetention = "30d"  // retain data for 30 days
};
using var channel = new IngestChannel<LogEntry>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
```

The channel auto-selects:
- `DataStreamIngestStrategy`: uses `create` operations (append-only)
- `DefaultBootstrapStrategy` with `ComponentTemplateStep` + `DataStreamTemplateStep`
- Data stream lifecycle with 30-day retention

## Writing data

### Continuous writer (long-lived)

For a service that continuously emits logs:

```csharp
using var channel = new IngestChannel<LogEntry>(options);
await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

// Write logs as they happen
logger.OnLog += entry => channel.TryWrite(entry);

// At shutdown
await channel.WaitForDrainAsync(TimeSpan.FromSeconds(10), ctx);
```

For high-throughput scenarios, tune the buffer:

```csharp
var options = new IngestChannelOptions<LogEntry>(transport, LoggingContext.LogEntry.Context)
{
    BufferOptions = new BufferOptions
    {
        InboundBufferMaxSize = 500_000,
        OutboundBufferMaxSize = 5_000,
        ExportMaxConcurrency = 8
    }
};
```

### Batch import (short-lived)

For importing historical log files:

```csharp
using var channel = new IngestChannel<LogEntry>(options);
await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

foreach (var entry in ParseLogFile("access.log"))
    channel.TryWrite(entry);

await channel.WaitForDrainAsync(TimeSpan.FromSeconds(60), ctx);
```

## Data retention

### Data stream lifecycle (recommended)

Set `DataStreamLifecycleRetention` on the channel options. This embeds the retention period in the index template and works on both serverless and self-managed Elasticsearch.

```csharp
options.DataStreamLifecycleRetention = "90d";
```

### ILM (self-managed only)

For multi-phase lifecycle policies (hot/warm/cold/delete), use ILM:

```csharp
var options = new IngestChannelOptions<LogEntry>(transport, LoggingContext.LogEntry.Context)
{
    IlmPolicy = "logs-policy",
    BootstrapStrategy = new DefaultBootstrapStrategy(
        new IlmPolicyStep("logs-policy", hotMaxAge: "7d", deleteMinAge: "90d"),
        new ComponentTemplateStep(),
        new DataStreamTemplateStep()
    )
};
```

See [ILM and lifecycle](../advanced/ilm-and-lifecycle.md) for details.

## Related

- [Data streams](../index-management/data-streams.md): data stream bootstrapping and naming
- [Data stream lifecycle](../index-management/rollover/data-stream-lifecycle.md): retention configuration
- [TSDB](../index-management/tsdb.md): time-series database mode for metrics
- [LogsDB](../index-management/logsdb.md): LogsDB mode and wired streams
- [Push model](../architecture/push-model.md): buffer tuning for high throughput
