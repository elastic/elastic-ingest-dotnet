---
navigation_title: LogsDB
---

# LogsDB

LogsDB mode optimizes data streams for log storage with synthetic `_source` and automatic field mapping. It is a `[DataStream<T>]` option — not a separate entity target.

## Configuration

```csharp
[ElasticsearchMappingContext]
[DataStream<LogEntry>(
    Type = "logs",
    Dataset = "myapp",
    Namespace = "production",
    DataStreamMode = DataStreamMode.LogsDb
)]
public static partial class LogsContext;
```

LogsDB data streams work the same as regular data streams but with storage optimizations applied automatically by Elasticsearch. Bootstrap still creates component and data stream templates (classic Streams path).

## Wired streams

For managed Kibana **wired** streams (no local bootstrap, `logs.ecs` / `logs.otel` endpoints), see [Wired streams](wired-streams.md). LogsDB mode does not apply to `[WiredStream<T>]`.

## Related

- [Data streams](data-streams.md): standard data stream bootstrapping
- [Streams](streams.md): classic vs wired in Kibana Streams
- [Time-series](../use-cases/time-series.md): end-to-end time-series guide
