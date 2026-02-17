# Elastic.Ingest.Transport

Integrates the [`Elastic.Transport`](https://github.com/elastic/elastic-transport-net) HTTP layer with the [`Elastic.Channels`](https://www.nuget.org/packages/Elastic.Channels) buffering infrastructure.

Most users install [`Elastic.Ingest.Elasticsearch`](https://www.nuget.org/packages/Elastic.Ingest.Elasticsearch) which pulls this package in as a transitive dependency.

## What it provides

- **`TransportChannelBase<TChannelOptions, TEvent, TResponse>`** — abstract channel that sends buffered batches over `ITransport`
- **Response-item handling** — per-item success/failure tracking from bulk responses
- **AOT compatibility** — supports Native AOT on `net8.0+`

## Where it sits

```
Elastic.Ingest.Elasticsearch   ← strategies, channels, orchestrators
        ↓
Elastic.Ingest.Transport       ← ITransport integration (this package)
        ↓
Elastic.Channels               ← buffered channel infrastructure
```

## Documentation

Full documentation: **<https://elastic.github.io/elastic-ingest-dotnet/>**

- [Architecture](https://elastic.github.io/elastic-ingest-dotnet/architecture/) — package layering and the push model
- [Transport layer](https://elastic.github.io/elastic-ingest-dotnet/architecture/transport-layer/) — how this package bridges channels and Elasticsearch
