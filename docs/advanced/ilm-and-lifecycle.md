---
navigation_title: ILM and lifecycle
---

# ILM vs Data Stream Lifecycle

Elastic.Ingest.Elasticsearch supports two approaches to data retention management: ILM (Index Lifecycle Management) and Data Stream Lifecycle (DSL).

## ILM (Index Lifecycle Management)

ILM is the traditional approach for managing index lifecycle on self-managed and Elastic Cloud clusters. It defines phases (hot, warm, cold, frozen, delete) with actions and conditions.

### Using BootstrapStrategies factory

The simplest way to add ILM is through the `BootstrapStrategies.DataStreamWithIlm` factory:

```csharp
var strategy = IngestStrategies.DataStream<LogEntry>(
    LoggingContext.LogEntry.Context,
    BootstrapStrategies.DataStreamWithIlm("my-policy", hotMaxAge: "30d", deleteMinAge: "90d"));
var options = new IngestChannelOptions<LogEntry>(transport, strategy, LoggingContext.LogEntry.Context);
```

For indices, use `BootstrapStrategies.IndexWithIlm`:

```csharp
var strategy = IngestStrategies.Index<Product>(
    CatalogContext.Product.Context,
    BootstrapStrategies.IndexWithIlm("my-policy", hotMaxAge: "30d", deleteMinAge: "90d"));
var options = new IngestChannelOptions<Product>(transport, strategy, CatalogContext.Product.Context);
```

The factory creates an `IlmPolicyStep` that:
- Creates the ILM policy via `PUT _ilm/policy/{name}`
- Skips on serverless (where ILM is not supported)
- Checks if the policy already exists before creating (idempotent)
- Is ordered **before** `ComponentTemplateStep` since the component template references the policy by name

### Custom policy JSON

For full control over ILM phases:

```csharp
var policyJson = """
{
    "phases": {
        "hot": { "actions": { "rollover": { "max_age": "7d", "max_primary_shard_size": "50gb" } } },
        "warm": { "min_age": "30d", "actions": { "shrink": { "number_of_shards": 1 } } },
        "delete": { "min_age": "90d", "actions": { "delete": {} } }
    }
}
""";
var bootstrap = new DefaultBootstrapStrategy(
    new IlmPolicyStep("my-policy", policyJson),
    new ComponentTemplateStep("my-policy"),
    new DataStreamTemplateStep()
);
var strategy = IngestStrategies.DataStream<LogEntry>(
    LoggingContext.LogEntry.Context, bootstrap);
var options = new IngestChannelOptions<LogEntry>(transport, strategy, LoggingContext.LogEntry.Context);
```

### ILM policy reference

The `ComponentTemplateStep` automatically adds `"index.lifecycle.name"` to the settings component template when `BootstrapContext.IlmPolicy` is set (and not on serverless).

## Data Stream Lifecycle (DSL)

DSL is the serverless-compatible alternative to ILM. It specifies a data retention period embedded directly in the index template.

### Using IngestStrategies factory

The simplest approach -- pass a retention period to `IngestStrategies.DataStream`:

```csharp
var strategy = IngestStrategies.DataStream<LogEntry>(LoggingContext.LogEntry.Context, "30d");
var options = new IngestChannelOptions<LogEntry>(transport, strategy, LoggingContext.LogEntry.Context);
using var channel = new IngestChannel<LogEntry>(options);
```

This produces a template with:
```json
{
    "template": { "lifecycle": { "data_retention": "30d" } },
    "data_stream": {}
}
```

### Using BootstrapStrategies.DataStream explicitly

For explicit bootstrap strategy composition:

```csharp
var strategy = IngestStrategies.DataStream<LogEntry>(
    LoggingContext.LogEntry.Context,
    BootstrapStrategies.DataStream("30d"));
var options = new IngestChannelOptions<LogEntry>(transport, strategy, LoggingContext.LogEntry.Context);
```

## Choosing between ILM and DSL

| Feature | ILM | DSL |
|---------|-----|-----|
| Serverless support | No | Yes |
| Multiple phases | Yes (hot/warm/cold/frozen/delete) | No (retention only) |
| Rollover conditions | Yes (age, size, docs) | Automatic |
| Complexity | Higher | Lower |
| Recommended for | Self-managed, complex lifecycle | Serverless, simple retention |

**Recommendation**: Use DSL (`IngestStrategies.DataStream` with a retention period) for new projects unless you need multi-phase ILM policies. DSL works on both serverless and self-managed clusters.
