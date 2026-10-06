---
navigation_title: ECS and OTel endpoints
---

# ECS and OTel endpoints

Wired streams expose specialized bulk endpoints that control how field names are stored. See Elastic's [wired streams field naming](https://www.elastic.co/docs/solutions/observability/streams/wired-streams-field-naming) for the full conversion table.

## Endpoints

| Endpoint | Path | Behavior |
|----------|------|----------|
| `WiredStreamIngestEndpoint.Logs` | `logs/_bulk` | Default; backward-compatible generic wired path |
| `WiredStreamIngestEndpoint.LogsEcs` | `logs.ecs/_bulk` | Stores ECS field names as sent |
| `WiredStreamIngestEndpoint.LogsOtel` | `logs.otel/_bulk` | Stores OTel semantic conventions; creates ECS aliases for existing queries |

```csharp
[ElasticsearchMappingContext]
[WiredStream<EcsLog>(
    Type = "logs",
    Dataset = "dotnet",
    IngestEndpoint = WiredStreamIngestEndpoint.LogsEcs)]
public static partial class EcsWiredContext;
```

## Document shape for `logs.ecs`

Documents should use Elastic Common Schema field names. A minimal shape:

```csharp
public class EcsLog
{
    [Timestamp]
    [JsonPropertyName("@timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }

    [JsonPropertyName("log.level")]
    public string LogLevel { get; set; }

    [JsonPropertyName("service.name")]
    public string ServiceName { get; set; }

    [JsonPropertyName("host.name")]
    public string? HostName { get; set; }

    [JsonPropertyName("trace.id")]
    public string? TraceId { get; set; }
}
```

Useful fields for Kibana Streams features (significant events, knowledge indicators, Streamlang conditions):

- `@timestamp` — required for time-based views
- `message` / `body.text` — free-text used by extraction and queries
- `service.*`, `host.*` — entity identity for topology and knowledge indicators
- `log.level` / `severity_text` — filtering and alerting
- `trace.id`, `transaction.id` — correlation with APM

## Integration with ecs-dotnet

[elastic/ecs-dotnet](https://github.com/elastic/ecs-dotnet) formats logs as ECS JSON and ships via Serilog, NLog, or Microsoft.Extensions.Logging.

### Path A — Classic data stream (existing sinks)

`Elastic.Serilog.Sinks` and related shippers use a classic data stream (`logs-dotnet-default` by default) with optional template bootstrap. That path maps to `[DataStream<T>]` in this library and appears as a **classic stream** in Kibana Streams.

```csharp
// ecs-dotnet Serilog sink (classic)
.WriteTo.Elasticsearch(nodes, opts =>
{
    opts.DataStream = new DataStreamName("logs", "dotnet", "production");
    opts.BootstrapMethod = BootstrapMethod.Failure;
})
```

### Path B — Wired streams (this library)

For Kibana **wired** streams, send ECS documents through `IngestChannel` with `LogsEcs`:

```csharp
[ElasticsearchMappingContext]
[WiredStream<LogEventEcsDocument>(
    Type = "logs",
    Dataset = "dotnet",
    IngestEndpoint = WiredStreamIngestEndpoint.LogsEcs)]
public static partial class WiredEcsContext;

// ...
var options = new IngestChannelOptions<LogEventEcsDocument>(
    transport, WiredEcsContext.LogEventEcsDocument.Context);
using var channel = new IngestChannel<LogEventEcsDocument>(options);
await channel.BootstrapElasticsearchAsync(BootstrapMethod.None);
channel.TryWrite(ecsDocument);
```

Until ecs-dotnet sinks expose a first-class wired option, Path B is the supported way to target `logs.ecs` from .NET.

### Path C — OpenTelemetry

Use `IngestEndpoint = LogsOtel` when documents follow OTel log semantic conventions (or when you want Streams to normalize ECS → OTel storage). Prefer this for OpenTelemetry Collector / OTLP-shaped payloads rather than ecs-dotnet formatters.

## Related

- [Wired streams](wired-streams.md)
- [Streams overview](streams.md)
- [Downstream Streams features](streams-downstream-features.md)
