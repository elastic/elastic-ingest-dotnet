---
navigation_title: TSDB metrics
---

# TSDB metrics

TSDB (time-series database) mode optimizes data streams for metrics storage. It enables sorted indices, synthetic `_source`, automatic deduplication, and downsampling -- significantly reducing storage for high-cardinality metrics.

## When to use

- Ingesting metrics (CPU, memory, request latency, business KPIs)
- You have natural dimension fields (host, service, metric name)
- You want Elasticsearch's time-series optimizations (deduplication, downsampling)

## Document type

TSDB requires at least one `[Dimension]` field and a `[Timestamp]`:

```csharp
public class MetricEvent
{
    [Timestamp]
    [JsonPropertyName("@timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [Dimension]
    [Keyword]
    public string Host { get; set; }

    [Dimension]
    [Keyword]
    public string MetricName { get; set; }

    public double Value { get; set; }

    [Keyword]
    public string Unit { get; set; }
}
```

The combination of dimension fields and timestamp uniquely identifies a data point. Documents with the same dimensions and timestamp are deduplicated.

## Mapping context

```csharp
[ElasticsearchMappingContext]
[DataStream<MetricEvent>(
    Type = "metrics",
    Dataset = "myapp",
    Namespace = "production",
    DataStreamMode = DataStreamMode.Tsdb
)]
public static partial class MetricsContext;
```

## Channel setup

```csharp
var strategy = IngestStrategies.DataStream<MetricEvent>(MetricsContext.MetricEvent.Context, "90d");
var options = new IngestChannelOptions<MetricEvent>(transport, strategy, MetricsContext.MetricEvent.Context);
using var channel = new IngestChannel<MetricEvent>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
```

## Writing metrics

For continuous metrics collection, tune the buffer for throughput:

```csharp
var strategy = IngestStrategies.DataStream<MetricEvent>(MetricsContext.MetricEvent.Context, "90d");
var options = new IngestChannelOptions<MetricEvent>(transport, strategy, MetricsContext.MetricEvent.Context)
{
    BufferOptions = new BufferOptions
    {
        InboundBufferMaxSize = 500_000,
        OutboundBufferMaxSize = 5_000,
        ExportMaxConcurrency = 8
    }
};
using var channel = new IngestChannel<MetricEvent>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

// Collect metrics on a timer
timer.Elapsed += (_, _) =>
{
    channel.TryWrite(new MetricEvent
    {
        Timestamp = DateTimeOffset.UtcNow,
        Host = Environment.MachineName,
        MetricName = "cpu.usage",
        Value = GetCpuUsage(),
        Unit = "percent"
    });
};

// At shutdown
await channel.WaitForDrainAsync(TimeSpan.FromSeconds(10), ctx);
```

## Related

- [TSDB](../index-management/tsdb.md): TSDB mode configuration reference
- [Time-series](time-series.md): standard data stream ingestion
- [Data streams](../index-management/data-streams.md): data stream naming and bootstrap
