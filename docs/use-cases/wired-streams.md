---
navigation_title: Wired streams
---

# Wired streams (serverless)

Wired streams are the simplest ingestion path. Elasticsearch manages all templates and lifecycle -- you just send documents.

## When to use

- Serverless Elasticsearch
- Log data that doesn't need custom mappings or templates
- You want the minimum possible configuration

## Document type

```csharp
public class LogEntry
{
    [Timestamp]
    [JsonPropertyName("@timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [Keyword]
    public string Level { get; set; }

    [Text]
    public string Message { get; set; }

    [Keyword]
    public string Service { get; set; }
}
```

## Mapping context

```csharp
[ElasticsearchMappingContext]
[Entity<LogEntry>(Target = EntityTarget.WiredStream)]
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
| Ingest | `WiredStreamIngestStrategy` | Sends to `logs/_bulk` endpoint |
| Bootstrap | `NoopBootstrapStep` | Elasticsearch manages all templates |
| Provisioning | `AlwaysCreateProvisioning` | No local index management |
| Alias | `NoAliasStrategy` | Elasticsearch manages routing |

## Related

- [LogsDB](../index-management/logsdb.md): LogsDB mode and wired stream details
- [Time-series](time-series.md): data streams with local template management
