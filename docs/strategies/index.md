---
navigation_title: Strategies
---

# Strategies

Elastic.Ingest.Elasticsearch uses a composable strategy pattern. Each aspect of channel behavior is encapsulated in a strategy interface, allowing you to mix and match implementations or create your own.

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
