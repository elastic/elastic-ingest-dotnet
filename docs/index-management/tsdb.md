---
navigation_title: TSDB
---

# TSDB (time-series database mode)

TSDB mode optimizes data streams for metrics storage. It requires at least one dimension field and a timestamp, enabling Elasticsearch to apply time-series specific optimizations (sorted indices, doc-value-only fields, synthetic `_source`).

## Requirements

- `[Timestamp]` field (required for all data streams)
- At least one `[Dimension]` field (identifies the time-series)
- `EntityTarget.DataStream` with `DataStreamMode.Tsdb`

## Document type

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

Dimension fields identify what the metric is about (host, service, metric name). The combination of dimensions and timestamp uniquely identifies a data point.

## Configuration

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

## TSDB benefits

- **Storage efficiency**: sorted indices and synthetic `_source` reduce storage significantly
- **Automatic deduplication**: documents with the same dimensions and timestamp are deduplicated
- **Downsampling support**: Elasticsearch can aggregate old data into lower-resolution summaries

## Related

- [Data streams](data-streams.md): general data stream concepts
- [Time-series](../use-cases/time-series.md): end-to-end time-series guide
