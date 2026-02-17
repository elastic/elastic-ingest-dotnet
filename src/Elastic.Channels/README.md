# Elastic.Channels

A thread-safe, batching `ChannelWriter` for high-throughput data pipelines.

Most users install [`Elastic.Ingest.Elasticsearch`](https://www.nuget.org/packages/Elastic.Ingest.Elasticsearch) which pulls this package in as a transitive dependency.

## What it provides

- **Automatic batching** — flushes when the buffer hits a max count _or_ a max age, whichever comes first
- **Concurrent export** — configurable parallelism for sending batches
- **Retry with backoff** — configurable retry count and backoff function
- **Backpressure** — bounded inbound buffer with `BoundedChannelFullMode` control (drop or wait)

## BufferOptions

Each channel exposes a `BufferOptions` instance that controls buffering behavior:

| Option                      | Description                                                                                                                  |
|-----------------------------|------------------------------------------------------------------------------------------------------------------------------|
| `InboundBufferMaxSize`      | The maximum number of in flight instances that can be queued in memory. If this threshold is reached, events will be dropped |
| `OutboundBufferMaxSize`     | The number of events a local buffer should reach before sending the events in a single call to Elasticsearch.                |
| `OutboundBufferMaxLifetime` | The maximum age of buffer before its flushed                                                                                 |
| `ExportMaxConcurrency`      | Controls how many concurrent `Export` operations may occur                                                                   |
| `ExportMaxRetries`          | The maximum number of retries over `Export`                                                                                  |
| `ExportBackOfPeriod`        | Func that calculates an appropriate backoff time for a retry                                                                 |
| `ExportBufferCallback`      | Called `once` whenever a buffer is flushed, excluding retries                                                                |
| `WaitHandle`                | Inject a waithandle that will be signalled after each flush, excluding retries.                                              |

## Documentation

Full documentation: **<https://elastic.github.io/elastic-ingest-dotnet/>**

- [Architecture](https://elastic.github.io/elastic-ingest-dotnet/architecture/) — how the two-stage buffered pipeline works
- [Channels](https://elastic.github.io/elastic-ingest-dotnet/channels/) — buffer tuning, callbacks, serialization
