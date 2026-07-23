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

## Version-aware bootstrap guards

By default, bootstrap uses **hash-only** comparison: if the template hash matches, skip; if it differs, re-bootstrap. This works well for single-version deployments, but can cause problems during rolling upgrades.

### The problem

Consider a pool of pods running behind a load balancer. You deploy version N, which upgrades the index template with new mappings. A pod still on version N-1 restarts, computes a different hash (its mappings are older), and re-bootstraps — **overwriting version N's templates** with version N-1's mappings.

### The solution: `MappingVersion`

Set `MappingVersion` on your index or data stream attribute. When configured, the version is stored in `_meta.mapping_version` on all templates. During bootstrap, if the remote template was deployed by a **newer** version, bootstrap is skipped — the older pod refuses to downgrade.

:::{note}
`MappingVersion` is **opt-in**. When omitted, behavior is unchanged — pure hash-based comparison.

This is separate from `_meta.assembly_version`, which always reflects the library version and is purely informational.
:::

### Usage

**Default — hash-only (no version guard):**

```csharp
[Index<Product>(Name = "products")]
```

No `MappingVersion` is set. Templates are updated whenever the hash changes, regardless of which version is deploying. This is the existing behavior.

**Explicit version (via attribute):**

```csharp
[Index<Product>(Name = "products", MappingVersion = "1.0.0")]

[DataStream<LogEntry>(Type = "logs", Dataset = "myapp", MappingVersion = "2.1.0")]
```

Set `MappingVersion` to a version string parseable by `System.Version` (e.g. `"1.0.0"`, `"2.3.1"`). Bump it when you release mapping changes.

**Application assembly version (via attribute):**

Use `MappingVersionFromAssembly` to automatically resolve the version from your application's assembly at runtime. The source generator emits code that reads `typeof(YourMappingContext).Assembly.GetName().Version` — so the version tracks your project's `<Version>` or `<AssemblyVersion>` MSBuild property:

```csharp
[Index<Product>(Name = "products", MappingVersionFromAssembly = true)]

[DataStream<LogEntry>(Type = "logs", Dataset = "myapp", MappingVersionFromAssembly = true)]
```

When both `MappingVersionFromAssembly` and `MappingVersion` are set, `MappingVersionFromAssembly` takes precedence.

**Programmatic usage:**

When not using attributes, set `MappingVersion` directly on `ElasticsearchTypeContext` or override a source-generated context:

```csharp
// Override a source-generated context with a runtime version
var appVersion = typeof(MyMappingContext).Assembly.GetName().Version?.ToString();
var context = MyMappingContext.Product.Context with { MappingVersion = appVersion };
var options = new IngestChannelOptions<Product>(transport, context);
```

Or set it on `BootstrapContext` when using custom bootstrap strategies:

```csharp
var bootstrapContext = new BootstrapContext
{
    Transport = transport,
    BootstrapMethod = BootstrapMethod.Failure,
    TemplateName = "my-template",
    TemplateWildcard = "my-index-*",
    MappingVersion = "1.2.0"
};
```

### Decision logic

When `MappingVersion` is set, bootstrap checks two conditions. **Both** must pass for bootstrap to be skipped — if either indicates a change is needed, bootstrap proceeds:

| Remote version vs local | Hash | Result |
|------------------------|------|--------|
| Remote **newer** than local | _any_ | **Skip** — don't downgrade |
| Equal or local newer | **Matches** | **Skip** — nothing changed |
| Equal or local newer | **Differs** | **Proceed** — templates changed, upgrade |
| Remote has no version | **Matches** | **Skip** — hash says nothing changed |
| Remote has no version | **Differs** | **Proceed** — hash says templates changed |

In other words:

1. **Version guard**: If the remote `mapping_version` is **strictly greater** than the local one (compared as `System.Version`), **skip**. The cluster already has a newer deployment's templates — don't overwrite them.
2. **Hash check**: If the content hashes match, **skip**. The templates are identical.
3. Otherwise: **proceed** with bootstrap. The local version is equal or newer and the templates have actually changed.

Bootstrap only proceeds when the hash differs **and** the remote version is not newer. This is the key protection: an older pod sees a different hash (because its mappings are older) but also sees that the remote version is higher, so it backs off.

When `MappingVersion` is *not* set (null), only the hash check applies — the original behavior.
