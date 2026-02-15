---
navigation_title: LogsDB
---

# LogsDB and wired streams

LogsDB mode and wired streams provide optimized log ingestion paths, particularly useful on serverless Elasticsearch.

## LogsDB mode

LogsDB mode optimizes data streams for log storage with synthetic `_source` and automatic field mapping.

```csharp
[ElasticsearchMappingContext]
[Entity<LogEntry>(
    Target = EntityTarget.DataStream,
    DataStreamType = "logs",
    DataStreamDataset = "myapp",
    DataStreamNamespace = "production",
    DataStreamMode = DataStreamMode.LogsDb
)]
public static partial class LogsContext;
```

LogsDB data streams work the same as regular data streams but with storage optimizations applied automatically by Elasticsearch.

## Wired streams

Wired streams are serverless managed ingestion endpoints. Unlike data streams, **no local bootstrap is needed** -- Elasticsearch manages templates and lifecycle entirely.

### Configuration

```csharp
[ElasticsearchMappingContext]
[Entity<LogEntry>(
    Target = EntityTarget.WiredStream
)]
public static partial class WiredContext;
```

### Channel setup

```csharp
var options = new IngestChannelOptions<LogEntry>(transport, WiredContext.LogEntry.Context);
using var channel = new IngestChannel<LogEntry>(options);

// Bootstrap is a no-op for wired streams
await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

foreach (var entry in logEntries)
    channel.TryWrite(entry);

await channel.WaitForDrainAsync(TimeSpan.FromSeconds(10), ctx);
```

### How it works

- `WiredStreamIngestStrategy` sends bulk requests to the `logs/_bulk` endpoint
- `NoopBootstrapStep` skips all template creation
- Documents use `create` operations (append-only)
- Elasticsearch manages all index templates, lifecycle, and retention

### When to use

- Serverless Elasticsearch where you want the simplest possible ingestion path
- Log data that doesn't need custom templates or mappings
- Scenarios where Elasticsearch should manage the full index lifecycle

## Related

- [Data streams](data-streams.md): standard data stream bootstrapping
- [Time-series](../getting-started/time-series.md): end-to-end time-series guide
