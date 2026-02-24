---
navigation_title: Data stream with ILM
---

# Data stream with ILM

Use ILM (Index Lifecycle Management) when you need multi-phase lifecycle policies: hot for active writes, warm for read-only optimization, cold for archival, and delete for cleanup.

## When to use

- Self-managed or Elastic Cloud clusters (ILM is **not** available on serverless)
- You need more than simple retention -- rollover conditions, phase transitions, shrink, force-merge
- You want Elasticsearch to manage rollover automatically based on age, size, or document count

For serverless or simple retention, use [data stream lifecycle](time-series.md) instead.

## Document type

```csharp
public class AuditEvent
{
    [Timestamp]
    [JsonPropertyName("@timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [Keyword]
    public string Action { get; set; }

    [Keyword]
    public string UserId { get; set; }

    [Text]
    public string Details { get; set; }

    [Keyword]
    public string Severity { get; set; }
}
```

## Mapping context

```csharp
[ElasticsearchMappingContext]
[DataStream<AuditEvent>(
    Type = "logs",
    Dataset = "audit",
    Namespace = "production"
)]
public static partial class AuditContext;
```

## Channel setup

```csharp
var strategy = IngestStrategies.DataStream<AuditEvent>(
    AuditContext.AuditEvent.Context,
    BootstrapStrategies.DataStreamWithIlm("audit-policy", hotMaxAge: "7d", deleteMinAge: "90d"));
var options = new IngestChannelOptions<AuditEvent>(transport, strategy, AuditContext.AuditEvent.Context);
using var channel = new IngestChannel<AuditEvent>(options);

await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
```

`BootstrapStrategies.DataStreamWithIlm` creates three bootstrap steps in order:
1. `IlmPolicyStep` -- creates `PUT _ilm/policy/audit-policy`
2. `ComponentTemplateStep` -- creates settings and mappings component templates (references the ILM policy)
3. `DataStreamTemplateStep` -- creates the index template

## Custom ILM phases

For full control over the policy, compose the bootstrap steps manually:

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
            "actions": { "shrink": { "number_of_shards": 1 }, "forcemerge": { "max_num_segments": 1 } }
        },
        "delete": {
            "min_age": "90d",
            "actions": { "delete": {} }
        }
    }
}
""";
var bootstrap = new DefaultBootstrapStrategy(
    new IlmPolicyStep("audit-policy", policyJson),
    new ComponentTemplateStep("audit-policy"),
    new DataStreamTemplateStep()
);
var strategy = IngestStrategies.DataStream<AuditEvent>(
    AuditContext.AuditEvent.Context, bootstrap);
var options = new IngestChannelOptions<AuditEvent>(transport, strategy, AuditContext.AuditEvent.Context);
```

## Writing events

```csharp
foreach (var evt in auditEvents)
    channel.TryWrite(evt);

await channel.WaitForDrainAsync(TimeSpan.FromSeconds(30), ctx);
```

## Related

- [ILM managed rollover](../index-management/rollover/ilm-managed.md): ILM configuration reference
- [ILM and lifecycle](../advanced/ilm-and-lifecycle.md): comparison of ILM vs data stream lifecycle
- [Time-series](time-series.md): data stream lifecycle (serverless-compatible alternative)
