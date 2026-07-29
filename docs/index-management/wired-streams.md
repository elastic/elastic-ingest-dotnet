---
navigation_title: Wired streams
---

# Wired streams

Wired streams send documents to a managed Elasticsearch bulk endpoint. Elasticsearch owns index templates, lifecycle, and retention — bootstrap in this library is a no-op.

## When to use

- Serverless Elasticsearch or Elastic Stack with wired streams enabled
- You want Kibana Streams hierarchy (parent/child routing) without managing templates
- Log data whose field naming follows ECS or OTel conventions (see [ECS and OTel endpoints](ecs-and-otel-endpoints.md))

Prefer `[DataStream<T>]` when you need custom mappings, ILM policies, or LogsDB/TSDB modes that you bootstrap yourself.

## Configuration

```csharp
[ElasticsearchMappingContext]
[WiredStream<LogEntry>(
    Type = "logs",
    Dataset = "myapp",
    IngestEndpoint = WiredStreamIngestEndpoint.LogsEcs)]
public static partial class WiredContext;
```

`Type`, `Dataset`, and optional `Namespace` still form the conventional `{type}-{dataset}-{namespace}` name used for search patterns and template naming helpers. They do **not** change the bulk URL — that comes from `IngestEndpoint`.

| `IngestEndpoint` | Bulk path | Field naming |
|------------------|-----------|--------------|
| `Logs` (default) | `logs/_bulk` | Legacy/generic wired endpoint |
| `LogsEcs` | `logs.ecs/_bulk` | ECS fields stored as-is |
| `LogsOtel` | `logs.otel/_bulk` | OTel semantic conventions (+ ECS aliases) |

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
| Ingest | `WiredStreamIngestStrategy` | Sends to `{endpoint}/_bulk` with `create` ops |
| Bootstrap | `NoopBootstrapStep` | Elasticsearch manages all templates |
| Provisioning | `AlwaysCreateProvisioning` | No local index management |
| Alias | `NoAliasStrategy` | Elasticsearch manages routing |

## Mappings and wired streams

Attribute-generated mappings on a `[WiredStream<T>]` document type are **not deployed**. There is no component template step. Field mappings are governed by the wired stream endpoint and Streams-managed schema.

Use mapping attributes for:

- Compile-time documentation of expected fields
- Serialization helpers (`[Timestamp]`, `[JsonPropertyName]`)
- Consistency with classic `[DataStream]` types you may also register

Do **not** expect `[Text]` / `[Keyword]` on a wired target to change cluster mappings.

## Related

- [Streams overview](streams.md): classic vs wired terminology
- [ECS and OTel endpoints](ecs-and-otel-endpoints.md): endpoint choice and ecs-dotnet
- [Use case: Wired streams](../use-cases/wired-streams.md): end-to-end example
- [Data streams](data-streams.md): classic path with local bootstrap
