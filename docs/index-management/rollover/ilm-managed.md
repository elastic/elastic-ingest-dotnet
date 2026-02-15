---
navigation_title: ILM managed
---

# ILM managed rollover

Index Lifecycle Management (ILM) automates rollover and data retention on self-managed and Elastic Cloud clusters. It is **not available on serverless** -- use [data stream lifecycle](data-stream-lifecycle.md) instead.

## Configuration

Use the `BootstrapStrategies.DataStreamWithIlm` factory to create a strategy with an ILM policy:

```csharp
var strategy = IngestStrategies.DataStream<LogEntry>(
    LoggingContext.LogEntry.Context,
    BootstrapStrategies.DataStreamWithIlm("logs-policy", hotMaxAge: "7d", deleteMinAge: "90d"));
var options = new IngestChannelOptions<LogEntry>(transport, strategy, LoggingContext.LogEntry.Context);
using var channel = new IngestChannel<LogEntry>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
```

The `BootstrapStrategies.DataStreamWithIlm` factory creates an `IlmPolicyStep` ordered **before** the component template step, so the `ComponentTemplateStep` can automatically add `index.lifecycle.name` to the settings component template.

For indices (not data streams), use `BootstrapStrategies.IndexWithIlm`:

```csharp
var strategy = IngestStrategies.Index<Product>(
    CatalogContext.Product.Context,
    BootstrapStrategies.IndexWithIlm("products-policy", hotMaxAge: "30d", deleteMinAge: "180d"));
var options = new IngestChannelOptions<Product>(transport, strategy, CatalogContext.Product.Context);
```

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

For full control over phases and actions, use `IlmPolicyStep` with raw JSON and compose a `DefaultBootstrapStrategy` manually:

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
var bootstrap = new DefaultBootstrapStrategy(
    new IlmPolicyStep("logs-policy", policyJson),
    new ComponentTemplateStep("logs-policy"),
    new DataStreamTemplateStep()
);
var strategy = IngestStrategies.DataStream<LogEntry>(
    LoggingContext.LogEntry.Context, bootstrap);
var options = new IngestChannelOptions<LogEntry>(transport, strategy, LoggingContext.LogEntry.Context);
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
