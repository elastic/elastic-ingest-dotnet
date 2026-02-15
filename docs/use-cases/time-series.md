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

var strategy = IngestStrategies.DataStream<LogEntry>(LoggingContext.LogEntry.Context, "30d");
var options = new IngestChannelOptions<LogEntry>(transport, strategy, LoggingContext.LogEntry.Context);
using var channel = new IngestChannel<LogEntry>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
```

The `IngestStrategies.DataStream` factory creates a strategy that:
- Uses `DataStreamIngestStrategy`: `create` operations (append-only)
- Bootstraps with `ComponentTemplateStep` + `DataStreamLifecycleStep` + `DataStreamTemplateStep`
- Sets 30-day data stream lifecycle retention

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
var strategy = IngestStrategies.DataStream<LogEntry>(LoggingContext.LogEntry.Context, "30d");
var options = new IngestChannelOptions<LogEntry>(transport, strategy, LoggingContext.LogEntry.Context)
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

Use the `IngestStrategies.DataStream` factory with a retention period. This embeds the retention in the index template and works on both serverless and self-managed Elasticsearch.

```csharp
var strategy = IngestStrategies.DataStream<LogEntry>(LoggingContext.LogEntry.Context, "90d");
var options = new IngestChannelOptions<LogEntry>(transport, strategy, LoggingContext.LogEntry.Context);
```

### ILM (self-managed only)

For multi-phase lifecycle policies (hot/warm/cold/delete), use the `BootstrapStrategies.DataStreamWithIlm` factory:

```csharp
var strategy = IngestStrategies.DataStream<LogEntry>(
    LoggingContext.LogEntry.Context,
    BootstrapStrategies.DataStreamWithIlm("logs-policy", hotMaxAge: "7d", deleteMinAge: "90d"));
var options = new IngestChannelOptions<LogEntry>(transport, strategy, LoggingContext.LogEntry.Context);
```

See [ILM and lifecycle](../advanced/ilm-and-lifecycle.md) for details.

## Related

- [Data streams](../index-management/data-streams.md): data stream bootstrapping and naming
- [Data stream lifecycle](../index-management/rollover/data-stream-lifecycle.md): retention configuration
- [TSDB](../index-management/tsdb.md): time-series database mode for metrics
- [LogsDB](../index-management/logsdb.md): LogsDB mode and wired streams
- [Push model](../architecture/push-model.md): buffer tuning for high throughput
