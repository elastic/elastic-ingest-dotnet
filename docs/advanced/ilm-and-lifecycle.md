---
navigation_title: ILM and lifecycle
---

# ILM vs Data Stream Lifecycle

Elastic.Ingest.Elasticsearch supports two approaches to data retention management: ILM (Index Lifecycle Management) and Data Stream Lifecycle (DSL).

## ILM (Index Lifecycle Management)

ILM is the traditional approach for managing index lifecycle on self-managed and Elastic Cloud clusters. It defines phases (hot, warm, cold, frozen, delete) with actions and conditions.

### Using IlmPolicyStep

```csharp
var strategy = new DefaultBootstrapStrategy(
    new IlmPolicyStep("my-policy", hotMaxAge: "30d", deleteMinAge: "90d"),
    new ComponentTemplateStep(),
    new DataStreamTemplateStep()
);
```

The `IlmPolicyStep`:
- Creates the ILM policy via `PUT _ilm/policy/{name}`
- Skips on serverless (where ILM is not supported)
- Checks if the policy already exists before creating (idempotent)
- Should be ordered **before** `ComponentTemplateStep` since the component template references the policy by name

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
var step = new IlmPolicyStep("my-policy", policyJson);
```

### ILM policy reference

The `ComponentTemplateStep` automatically adds `"index.lifecycle.name"` to the settings component template when `BootstrapContext.IlmPolicy` is set (and not on serverless).

## Data Stream Lifecycle (DSL)

DSL is the serverless-compatible alternative to ILM. It specifies a data retention period embedded directly in the index template.

### Using DataStreamLifecycleRetention option

The simplest approach -- set the option and the channel auto-inserts the step:

```csharp
var options = new IngestChannelOptions<LogEntry>(transport, MyContext.LogEntry)
{
    DataStreamLifecycleRetention = "30d"
};
var channel = new IngestChannel<LogEntry>(options);
```

This produces a template with:
```json
{
    "template": { "lifecycle": { "data_retention": "30d" } },
    "data_stream": {}
}
```

### Using DataStreamLifecycleStep explicitly

For manual bootstrap strategy composition:

```csharp
var strategy = new DefaultBootstrapStrategy(
    new ComponentTemplateStep(),
    new DataStreamLifecycleStep("30d"),
    new DataStreamTemplateStep()
);
```

## Choosing between ILM and DSL

| Feature | ILM | DSL |
|---------|-----|-----|
| Serverless support | No | Yes |
| Multiple phases | Yes (hot/warm/cold/frozen/delete) | No (retention only) |
| Rollover conditions | Yes (age, size, docs) | Automatic |
| Complexity | Higher | Lower |
| Recommended for | Self-managed, complex lifecycle | Serverless, simple retention |

**Recommendation**: Use DSL (`DataStreamLifecycleRetention`) for new projects unless you need multi-phase ILM policies. DSL works on both serverless and self-managed clusters.
