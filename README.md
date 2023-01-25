# `Elastic.Ingest.*`

This repository houses various `Elastic.Ingest.*` packages that utilize `Elastic.Channels` to send bulk data to various (Elastic) endpoints.

### Projects

* [Elastic.Channels](src/Elastic.Channels/README.md) - core library that implements a batching `System.Threading.Channels.ChannelWriter`
* [Elastic.Ingest.Transport](src/Elastic.Ingest.Transport/README.md) - core library that ships common setup for pushing data utilizing [Elastic.Transport](https://github.com/elastic/elastic-transport-net)
* [Elastic.Ingest.Elasticsearch](src/Elastic.Ingest.Elasticsearch/README.md) - exposes `DataStreamChannel` and `IndexChannel` to push data to Elasticsearch with great ease.

#### in development 
* [Elastic.Ingest.APM](src/Elastic.Ingest.Apm/README.md) - Pushes APM data to apm-server over the V2 intake API. Still under development.

#### No plans of releasing
* [Elastic.Ingest.OpenTelemetry](src/Elastic.Ingest.OpenTelemetry/README.md) - a toy implementation of `Elastic.Channels` that pushes `Activities` over `OTLP`

