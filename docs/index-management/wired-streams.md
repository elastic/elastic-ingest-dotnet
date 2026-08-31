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

Wired streams **do support field mappings** — they are managed by [Kibana Streams](https://www.elastic.co/docs/solutions/observability/streams/map-fields) (Schema / Processing tabs), not by this library’s bootstrap.

What “bootstrap is a no-op” means here:

| Who | What happens for `[WiredStream<T>]` |
|-----|--------------------------------------|
| This library | Does **not** PUT component/index templates from your `[Text]` / `[Keyword]` attributes |
| Elasticsearch / Streams | Owns templates and lifecycle; you map fields in the Streams UI (or Streams API). Wired children can **inherit** parent mappings |
| Ingest endpoint | `logs.ecs` / `logs.otel` controls field-name normalization at ingest ([field naming](https://www.elastic.co/docs/solutions/observability/streams/wired-streams-field-naming)) |

Unmapped fields can still be queried (e.g. ES|QL `SET unmapped_fields = 'LOAD'`). Prefer ECS/OTel document shapes so Streams schema and processors line up.

Use mapping attributes on wired document types for serialization and shared type definitions with classic `[DataStream]` targets — not as a way to install cluster templates via `BootstrapElasticsearchAsync`.

For local template ownership (including LogsDB), use `[DataStream<T>]` instead.

## Related

- [Streams overview](streams.md): classic vs wired terminology
- [ECS and OTel endpoints](ecs-and-otel-endpoints.md): endpoint choice and ecs-dotnet
- [Use case: Wired streams](../use-cases/wired-streams.md): end-to-end example
- [Data streams](data-streams.md): classic path with local bootstrap
