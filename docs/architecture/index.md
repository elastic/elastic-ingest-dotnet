---
navigation_title: Architecture
---

# Architecture

Elastic.Ingest.Elasticsearch is built from three layered packages, each with a distinct responsibility.

## Packages

| Package | Responsibility |
|---------|---------------|
| `Elastic.Channels` | Thread-safe buffered channel with backpressure, batching, concurrent export, and retry |
| `Elastic.Ingest.Transport` | Integrates `Elastic.Transport` (`ITransport`) into the channel pipeline |
| `Elastic.Ingest.Elasticsearch` | Elasticsearch bulk API, composable strategies, orchestrators |

```
┌───────────────────────────────────┐
│  Elastic.Ingest.Elasticsearch     │  strategies, channels, orchestrators
├───────────────────────────────────┤
│  Elastic.Ingest.Transport         │  ITransport integration
├───────────────────────────────────┤
│  Elastic.Channels                 │  buffered channel infrastructure
└───────────────────────────────────┘
```

You only need to install `Elastic.Ingest.Elasticsearch`. The other packages are pulled in as transitive dependencies.

## Topics

- [Push model](push-model.md): how the two-stage buffer moves documents from producers to Elasticsearch
- [Channel hierarchy](channel-hierarchy.md): the class hierarchy from `BufferedChannelBase` to `IngestChannel<T>`
- [Transport layer](transport-layer.md): how `Elastic.Ingest.Transport` bridges channels and Elasticsearch
