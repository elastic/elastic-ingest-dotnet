---
navigation_title: Wired streams
---

# Wired streams (serverless)

Wired streams are the simplest managed ingestion path. Elasticsearch owns templates and lifecycle — you send documents to a wired bulk endpoint.

For the full reference (endpoints, mappings caveats, Kibana Streams), see [Wired streams](../index-management/wired-streams.md) under index management.

## When to use

- Serverless Elasticsearch or Elastic Stack with wired streams enabled
- ECS or OTel log documents that should land in Kibana Streams
- You want the minimum local configuration (no template bootstrap)

## Document type (ECS-shaped)

```csharp
public class LogEntry
{
    [Timestamp]
    [JsonPropertyName("@timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("log.level")]
    public string Level { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }

    [JsonPropertyName("service.name")]
    public string Service { get; set; }
}
```

## Mapping context

Prefer `LogsEcs` when sending Elastic Common Schema documents (including payloads from [ecs-dotnet](https://github.com/elastic/ecs-dotnet)):

```csharp
[ElasticsearchMappingContext]
[WiredStream<LogEntry>(
    Type = "logs",
    Dataset = "myapp",
    IngestEndpoint = WiredStreamIngestEndpoint.LogsEcs)]
public static partial class WiredContext;
```

## Channel setup

```csharp
var options = new IngestChannelOptions<LogEntry>(transport, WiredContext.LogEntry.Context);
using var channel = new IngestChannel<LogEntry>(options);

// Bootstrap is a no-op for wired streams
await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

foreach (var entry in logEntries)
    channel.TryWrite(entry);

await channel.WaitForDrainAsync(TimeSpan.FromSeconds(10), ctx);
```

## What the channel infers

| Behavior | Strategy | Why |
|----------|----------|-----|
| Ingest | `WiredStreamIngestStrategy` | Sends to `logs.ecs/_bulk` (or `logs` / `logs.otel`) |
| Bootstrap | `NoopBootstrapStep` | Elasticsearch manages all templates |
| Provisioning | `AlwaysCreateProvisioning` | No local index management |
| Alias | `NoAliasStrategy` | Elasticsearch manages routing |

## Related

- [Wired streams](../index-management/wired-streams.md): reference
- [ECS and OTel endpoints](../index-management/ecs-and-otel-endpoints.md): endpoint choice and ecs-dotnet paths
- [Streams](../index-management/streams.md): classic vs wired
- [Time-series](time-series.md): data streams with local template management
