---
navigation_title: Use cases
---

# Use cases

End-to-end guides showing how to apply the library to common ingestion patterns, from simplest to most complex.

| Use case | Pattern | Key features |
|----------|---------|-------------|
| [Wired streams](wired-streams.md) | Serverless log ingestion | Zero config, Elasticsearch manages everything |
| [Time-series](time-series.md) | Append-only logs, metrics, events | Data streams, lifecycle retention, high throughput |
| [Data stream with ILM](data-stream-ilm.md) | Multi-phase lifecycle for logs/events | ILM policies, hot/warm/cold/delete phases |
| [TSDB metrics](tsdb-metrics.md) | High-cardinality metrics | TSDB mode, dimension fields, deduplication |
| [E-commerce](e-commerce.md) | Product catalog sync with upserts | `[Id]` for upserts, alias swapping, hash-based reuse |
| [Manual rollover](manual-rollover.md) | Application-controlled rollover | Programmatic rollover triggers, condition-based |
| [Catalog data](catalog-data.md) | Versioned snapshots with dual-index orchestration | Multi-channel sync, schema migration, reindex vs multiplex |
| [Semantic enrichment](semantic-enrichment.md) | Dual-index with inference | Orchestrator, ELSER, lexical + semantic search |
