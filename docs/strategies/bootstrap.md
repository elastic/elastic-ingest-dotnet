---
navigation_title: Bootstrap
---

# Bootstrap strategies

Bootstrap strategies control how Elasticsearch infrastructure (templates, policies, endpoints) is created before ingestion begins.

## Why

Your channel needs Elasticsearch infrastructure to exist before it can write data: component templates define your mappings and settings, index templates tie them together, and optional ILM policies or lifecycle configurations manage data retention. Bootstrap strategies create all of this automatically so you don't have to manage it manually.

## BootstrapStrategies factory

The easiest way to create bootstrap strategies is through the `BootstrapStrategies` factory:

```csharp
// Data stream (component + data stream templates)
BootstrapStrategies.DataStream()

// Data stream with retention
BootstrapStrategies.DataStream("30d")

// Data stream with ILM
BootstrapStrategies.DataStreamWithIlm("my-policy", hotMaxAge: "7d", deleteMinAge: "90d")

// Index (component + index templates)
BootstrapStrategies.Index()

// Index with ILM
BootstrapStrategies.IndexWithIlm("my-policy", hotMaxAge: "30d", deleteMinAge: "180d")

// No-op (wired streams)
BootstrapStrategies.None()
```

Pass the result to an `IngestStrategies` factory method:

```csharp
var strategy = IngestStrategies.DataStream<LogEntry>(context,
    BootstrapStrategies.DataStreamWithIlm("logs-policy", hotMaxAge: "7d", deleteMinAge: "90d"));
var options = new IngestChannelOptions<LogEntry>(transport, strategy, context);
```

## IBootstrapStrategy

The `IBootstrapStrategy` interface orchestrates an ordered list of `IBootstrapStep` instances:

```csharp
public interface IBootstrapStrategy
{
    IReadOnlyList<IBootstrapStep> Steps { get; }
    Task<bool> BootstrapAsync(BootstrapContext context, CancellationToken ctx = default);
    bool Bootstrap(BootstrapContext context);
}
```

## DefaultBootstrapStrategy

The built-in implementation runs steps sequentially, stopping on the first failure:

```csharp
var strategy = new DefaultBootstrapStrategy(
    new ComponentTemplateStep(),
    new DataStreamTemplateStep()
);
```

## Bootstrap steps

Each step implements `IBootstrapStep` and performs a single bootstrap action:

| Step | Purpose |
|------|---------|
| `ComponentTemplateStep` | Creates settings and mappings component templates |
| `IndexTemplateStep` | Creates an index template (without data stream) |
| `DataStreamTemplateStep` | Creates an index template with `"data_stream": {}` |
| `InferenceEndpointStep` | Creates ELSER inference endpoints |
| `IlmPolicyStep` | Creates an ILM policy (skipped on serverless) |
| `DataStreamLifecycleStep` | Configures data stream lifecycle retention |
| `NoopBootstrapStep` | No-op step for wired streams |

## Bootstrap methods

The `BootstrapMethod` enum controls error handling:

| Method | Behavior |
|--------|----------|
| `None` | Skip bootstrap entirely |
| `Silent` | Return `false` on failure, don't throw |
| `Failure` | Throw an exception on failure |

## Step ordering

Steps run in the order they're provided. Common orderings:

**Data stream with ILM:**
```csharp
new DefaultBootstrapStrategy(
    new IlmPolicyStep("my-policy", hotMaxAge: "30d", deleteMinAge: "90d"),
    new ComponentTemplateStep(),
    new DataStreamTemplateStep()
);
```

**Data stream with lifecycle (serverless-compatible):**
```csharp
new DefaultBootstrapStrategy(
    new ComponentTemplateStep(),
    new DataStreamLifecycleStep("30d"),
    new DataStreamTemplateStep()
);
```

**Semantic index:**
```csharp
new DefaultBootstrapStrategy(
    new InferenceEndpointStep("my-elser"),
    new ComponentTemplateStep(),
    new IndexTemplateStep()
);
```

## Hash-based short-circuit

`IndexTemplateStep` and `DataStreamTemplateStep` check if the existing template's `_meta.hash` matches the current channel hash. If it matches, the template PUT is skipped, avoiding unnecessary API calls.
