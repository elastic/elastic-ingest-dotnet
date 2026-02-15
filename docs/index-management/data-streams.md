---
navigation_title: Data streams
---

# Data streams

Data streams are optimized for append-only time-series data. They handle index rollover and lifecycle automatically, using the naming convention `{type}-{dataset}-{namespace}`.

## Naming convention

| Component | Example | Description |
|-----------|---------|-------------|
| Type | `logs`, `metrics`, `traces` | Data category |
| Dataset | `myapp`, `nginx.access` | Source identifier |
| Namespace | `production`, `staging` | Environment |

Full name: `logs-myapp-production`

## Configuration

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

## Bootstrap

Data stream bootstrap creates:

1. **Component templates** via `ComponentTemplateStep`:
   - `logs-myapp-settings`: index settings
   - `logs-myapp-mappings`: field mappings
2. **Data stream template** via `DataStreamTemplateStep`:
   - Index pattern: `logs-myapp-*`
   - References component templates
   - Includes `"data_stream": {}` to enable data stream behavior
   - Infers built-in component templates based on type (for example, `logs-settings`, `logs-mappings` for logs type)

```csharp
var options = new IngestChannelOptions<LogEntry>(transport, LoggingContext.LogEntry.Context);
using var channel = new IngestChannel<LogEntry>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
```

## Ingest behavior

Data streams use `create` operations (append-only). Documents cannot be updated or deleted through the bulk API -- they can only be appended.

The `DataStreamIngestStrategy` is automatically selected for `EntityTarget.DataStream`.

## Lifecycle

Data streams support two lifecycle approaches:

- **Data stream lifecycle** (recommended): set `DataStreamLifecycleRetention` for automatic retention. See [data stream lifecycle](rollover/data-stream-lifecycle.md).
- **ILM** (self-managed only): attach an ILM policy for multi-phase lifecycle. See [ILM managed](rollover/ilm-managed.md).

## Related

- [Time-series](../getting-started/time-series.md): end-to-end guide for time-series data
- [TSDB](tsdb.md): time-series database mode for metrics
- [LogsDB](logsdb.md): LogsDB mode for log ingestion
