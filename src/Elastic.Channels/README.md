# Elastic.Channels

A thread-safe, batching `ChannelWriter` for high-throughput data pipelines.

Most users install [`Elastic.Ingest.Elasticsearch`](https://www.nuget.org/packages/Elastic.Ingest.Elasticsearch) which pulls this package in as a transitive dependency.

## What it provides

- **Automatic batching** — flushes when the buffer hits a max count, a max age, _or_ a max byte budget (netstandard2.1+/net8+), whichever comes first
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
| `OutboundBufferMaxBytes`    | Optional byte budget per outbound batch (netstandard2.1+/net8+). When set, a single outbound page is sliced into multiple sub-requests at export time so each stays within the limit (see below). |
| `ExportMaxConcurrency`      | Controls how many concurrent `Export` operations may occur                                                                   |
| `ExportMaxRetries`          | The maximum number of retries over `Export`                                                                                  |
| `ExportBackOfPeriod`        | Func that calculates an appropriate backoff time for a retry                                                                 |
| `ExportBufferCallback`      | Called `once` whenever a buffer is flushed, excluding retries                                                                |
| `WaitHandle`                | Inject a waithandle that will be signalled after each flush, excluding retries.                                              |

## Size-aware batching

> Requires **netstandard2.1 / net8.0** or later. Not available on netstandard2.0 — the property does not exist on that target, so there is no silent no-op.

When individual events vary significantly in size, a fixed item count can produce batches whose serialized body far exceeds the server's `max_coordinating_bytes` limit, causing 429 rejections. `OutboundBufferMaxBytes` adds a byte budget as a third early-flush condition alongside count and lifetime — whichever fires first wins.

```csharp
var options = new IngestChannelOptions<MyDoc>(transport, context)
{
    BufferOptions = new BufferOptions
    {
        OutboundBufferMaxSize  = 1_000,
        OutboundBufferMaxBytes = 100 * 1024 * 1024, // 100 MB — well under ES's 200 MB default
    }
};
```

**How it works — sub-batch at export time:**
When the budget is set, `ExportAsync` serializes each event **once** into a local buffer, tracks the running byte total, and cuts a new request when the total would exceed the limit. A single outbound page may become N sub-requests, each bounded by the budget. Each event is serialized exactly once — no double-serialize, no retained intermediate buffers.

```
page[0..N] → ExportAsync
  event[0]: serialize → 98 bytes → sub-batch 1 (98 bytes)
  event[1]: serialize → 102 bytes → 98+102=200 > budget? flush, start sub-batch 2
  event[2]: serialize → 95 bytes → sub-batch 2 (95 bytes)
  …
→ each sub-batch is a separate _bulk HTTP request
→ responses merged in page order → single BulkResponse to base retry loop
```

**Oversized individual events** (a single event larger than the budget alone) are emitted in their own sub-request. The `ItemExceedsBytesBudgetCallback` fires as a warning:

```csharp
var options = new IngestChannelOptions<MyDoc>(transport, context)
{
    BufferOptions = new BufferOptions { OutboundBufferMaxBytes = 100 * 1024 * 1024 },
    ItemExceedsBytesBudgetCallback = (doc, bytes) =>
        logger.LogWarning("Document {Id} is {Bytes:N0} bytes, exceeds batch budget", doc.Id, bytes),
};
```

**Memory:** during export, each concurrent export task holds at most `OutboundBufferMaxBytes` of serialized bytes in a local `MemoryStream`. Peak = `MaxConcurrency × OutboundBufferMaxBytes`. The bytes are freed when the export call returns.

**When `OutboundBufferMaxBytes` is `null` (default):** the streaming export path is used unchanged — no sub-batching, no extra memory, identical behaviour to previous releases.

## Documentation

Full documentation: **<https://elastic.github.io/elastic-ingest-dotnet/>**

- [Architecture](https://elastic.github.io/elastic-ingest-dotnet/architecture/) — how the two-stage buffered pipeline works
- [Channels](https://elastic.github.io/elastic-ingest-dotnet/channels/) — buffer tuning, callbacks, serialization
