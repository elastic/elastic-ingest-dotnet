---
navigation_title: ILM managed
---

# ILM managed rollover

Index Lifecycle Management (ILM) automates rollover and data retention on self-managed and Elastic Cloud clusters. It is **not available on serverless** -- use [data stream lifecycle](data-stream-lifecycle.md) instead.

## Configuration

Add an `IlmPolicyStep` to the bootstrap strategy, ordered **before** the component template step:

```csharp
var options = new IngestChannelOptions<LogEntry>(transport, MyContext.LogEntry.Context)
{
    IlmPolicy = "logs-policy",
    BootstrapStrategy = new DefaultBootstrapStrategy(
        new IlmPolicyStep("logs-policy", hotMaxAge: "7d", deleteMinAge: "90d"),
        new ComponentTemplateStep(),
        new DataStreamTemplateStep()
    )
};
```

The `ComponentTemplateStep` automatically adds `index.lifecycle.name` to the settings component template when `IlmPolicy` is set.

## ILM phases

ILM supports multiple phases:

| Phase | Purpose |
|-------|---------|
| Hot | Active writes and searches. Rollover triggers here. |
| Warm | Read-only, optimized for search. Shrink, force-merge. |
| Cold | Infrequent access. Searchable snapshots. |
| Frozen | Rare access. Partially mounted snapshots. |
| Delete | Remove the index entirely. |

## Custom policy JSON

For full control over phases and actions:

```csharp
var policyJson = """
{
    "phases": {
        "hot": {
            "actions": {
                "rollover": { "max_age": "7d", "max_primary_shard_size": "50gb" }
            }
        },
        "warm": {
            "min_age": "30d",
            "actions": { "shrink": { "number_of_shards": 1 } }
        },
        "delete": {
            "min_age": "90d",
            "actions": { "delete": {} }
        }
    }
}
""";
var step = new IlmPolicyStep("logs-policy", policyJson);
```

## Behavior

- `IlmPolicyStep` creates the policy via `PUT _ilm/policy/{name}`
- The step is idempotent: checks if the policy exists before creating
- On serverless clusters, the step is automatically skipped
- The policy must be created before the component template (which references it)

## Related

- [ILM and lifecycle](../../advanced/ilm-and-lifecycle.md): detailed comparison of ILM vs DSL
- [Data stream lifecycle](data-stream-lifecycle.md): serverless-compatible alternative
- [Bootstrap strategy](../../strategies/bootstrap.md): how bootstrap steps are ordered
