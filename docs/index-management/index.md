---
navigation_title: Index management
---

# Index management

Elastic.Ingest.Elasticsearch supports several index management strategies. The right choice depends on your data pattern, whether you need rollover, and whether you're running on serverless or self-managed Elasticsearch.

## Decision matrix

| Use case | Strategy | Serverless? | Guide |
|----------|----------|-------------|-------|
| Simple fixed index | Single index | Yes | [Single index](single-index.md) |
| Alias-based rotation | Manual alias swap | Yes | [Manual alias](rollover/manual-alias.md) |
| Condition-based rotation | Rollover API | Yes | [Rollover API](rollover/rollover-api.md) |
| Automatic lifecycle | ILM managed | No | [ILM managed](rollover/ilm-managed.md) |
| Simplified lifecycle | Data stream lifecycle | Yes | [Data stream lifecycle](rollover/data-stream-lifecycle.md) |
| Append-only time-series | Data streams | Yes | [Data streams](data-streams.md) |
| TSDB metrics | TSDB mode | Yes | [TSDB](tsdb.md) |
| Log ingestion | LogsDB / Wired Streams | Yes | [LogsDB](logsdb.md) |

## EntityTarget

The target-specific attribute on your mapping context (`[Index<T>]`, `[DataStream<T>]`, `[WiredStream<T>]`) controls which index management strategy the channel uses:

| EntityTarget | Description | Bootstrap |
|---|---|---|
| `Index` | Traditional Elasticsearch index. Supports updates, upserts, aliases. | Component + index templates |
| `DataStream` | Append-only data stream. Automatic rollover and lifecycle. | Component + data stream templates |
| `WiredStream` | Serverless managed stream. No local bootstrap needed. | No-op |

## How bootstrap works

When you call `BootstrapElasticsearchAsync`, the channel executes its bootstrap strategy steps in order:

1. **ComponentTemplateStep**: creates settings and mappings component templates, computes a content hash
2. **IndexTemplateStep** or **DataStreamTemplateStep**: creates the index template referencing the component templates
3. Optional steps: `IlmPolicyStep`, `DataStreamLifecycleStep`, `InferenceEndpointStep`

If the index template already exists with the same content hash, the PUT is skipped entirely. This makes bootstrap safe to call on every startup.
