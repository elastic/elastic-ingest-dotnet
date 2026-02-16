---
navigation_title: Strategies
---

# Strategies

Elastic.Ingest.Elasticsearch uses a composable strategy pattern. Each aspect of channel behavior is encapsulated in a strategy interface, allowing you to mix and match implementations or create your own.

## Why

Strategies let you customize channel behavior when auto-configuration isn't enough. The zero-config path (`IngestChannelOptions(transport, typeContext)`) works for most use cases, but when you need custom ILM policies, retention periods, or rollover behavior, strategies give you fine-grained control without forcing you to rewrite the entire pipeline.

## Factory methods

Most users interact with strategies through factory methods rather than constructing them directly.

### IngestStrategies

`IngestStrategies` creates fully composed `IIngestStrategy<TEvent>` instances:

```csharp
// Auto-detect from type context (zero-config)
var strategy = IngestStrategies.ForContext<MyDoc>(context);

// Data stream (default bootstrap)
var strategy = IngestStrategies.DataStream<LogEntry>(context);

// Data stream with retention
var strategy = IngestStrategies.DataStream<LogEntry>(context, "30d");

// Data stream with custom bootstrap
var strategy = IngestStrategies.DataStream<LogEntry>(context,
    BootstrapStrategies.DataStreamWithIlm("my-policy", hotMaxAge: "7d", deleteMinAge: "90d"));

// Index (default bootstrap)
var strategy = IngestStrategies.Index<Product>(context);

// Index with custom bootstrap
var strategy = IngestStrategies.Index<Product>(context,
    BootstrapStrategies.IndexWithIlm("my-policy"));

// Wired stream (no bootstrap)
var strategy = IngestStrategies.WiredStream<LogEntry>(context);
```

### BootstrapStrategies

`BootstrapStrategies` creates `IBootstrapStrategy` instances for use with `IngestStrategies`:

```csharp
// Data stream templates (no lifecycle)
BootstrapStrategies.DataStream()

// Data stream with retention
BootstrapStrategies.DataStream("30d")

// Data stream with ILM policy
BootstrapStrategies.DataStreamWithIlm("policy-name", hotMaxAge: "7d", deleteMinAge: "90d")

// Index templates
BootstrapStrategies.Index()

// Index with ILM policy
BootstrapStrategies.IndexWithIlm("policy-name")

// No-op (for wired streams)
BootstrapStrategies.None()
```

## Strategy types

| Strategy | Interface | Purpose |
|----------|-----------|---------|
| [Bootstrap](bootstrap.md) | `IBootstrapStrategy` | Creates templates, indices, and infrastructure |
| [Ingest](ingest.md) | `IDocumentIngestStrategy<T>` | Controls per-document bulk operation headers |
| [Provisioning](provisioning.md) | `IIndexProvisioningStrategy` | Decides whether to create or reuse indices |
| [Alias](alias.md) | `IAliasStrategy` | Manages aliases after indexing |
| [Rollover](rollover.md) | `IRolloverStrategy` | Triggers manual index/data stream rollover |

## Auto-resolution

When using `IngestChannel<T>` with an `ElasticsearchTypeContext`, strategies are auto-resolved based on the entity target:

| Entity target | Ingest | Bootstrap | Provisioning | Alias |
|---------------|--------|-----------|-------------|-------|
| DataStream | `DataStreamIngestStrategy` | Component + DataStream templates | Always create | No alias |
| Index | `TypeContextIndexIngestStrategy` | Component + Index templates | Hash-based reuse (if available) | Latest + search (if configured) |
| WiredStream | `WiredStreamIngestStrategy` | No-op | Always create | No alias |

## Custom strategies

You can implement any strategy interface to customize behavior. See [Custom strategies](../advanced/custom-strategies.md) for guidance.
