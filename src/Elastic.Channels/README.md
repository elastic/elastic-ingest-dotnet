# Elastic.Channels

A thread-safe, batching `ChannelWriter` for high-throughput data pipelines.

Most users install [`Elastic.Ingest.Elasticsearch`](https://www.nuget.org/packages/Elastic.Ingest.Elasticsearch) which pulls this package in as a transitive dependency.

## What it provides

- **Automatic batching** — flushes when the buffer hits a max count, a max age, _or_ a max byte budget, whichever comes first
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
| `OutboundBufferMaxBytes`    | Optional byte budget per outbound batch. When set, the buffer flushes early if accumulated event sizes exceed this value (see below). |
| `ExportMaxConcurrency`      | Controls how many concurrent `Export` operations may occur                                                                   |
| `ExportMaxRetries`          | The maximum number of retries over `Export`                                                                                  |
| `ExportBackOfPeriod`        | Func that calculates an appropriate backoff time for a retry                                                                 |
| `ExportBufferCallback`      | Called `once` whenever a buffer is flushed, excluding retries                                                                |
| `WaitHandle`                | Inject a waithandle that will be signalled after each flush, excluding retries.                                              |

## Size-aware batching

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

**How byte measurement works:**
The channel calls the virtual `CalculateOutboundBytesAsync` once per event as it enters the inbound buffer. The base implementation returns `0` (feature disabled). Subclasses override it to provide a size. For `Elastic.Ingest.Elasticsearch` channels this override is built in — it re-runs the exact NDJSON serialization the bulk request uses, writing to a discarding `CountingStream` so no intermediate buffer is ever allocated.

**Flush logic:**
- If the measured size of the next item plus the current batch size (or running average, for headroom) would exceed `OutboundBufferMaxBytes`, the existing batch is flushed first and the item starts a new one.
- If a single item individually exceeds the budget, it is emitted alone as a best-effort batch and the `ItemExceedsBytesBudgetCallback` fires.
- When `OutboundBufferMaxBytes` is `null` (the default), `CalculateOutboundBytesAsync` is never called and behaviour is identical to previous versions.

**Implementing for a custom channel:**

```csharp
public class MyChannel : BufferedChannelBase<MyOptions, MyEvent, MyResponse>
{
    protected override ValueTask<long> CalculateOutboundBytesAsync(
        MyEvent @event, CancellationToken ctx = default)
    {
        // Return the estimated serialized size — zero allocation, no buffering.
        return new(Encoding.UTF8.GetByteCount(@event.Payload));
    }
}
```

The `IWriteTrackingBuffer` passed to `ExportResponseCallback` exposes `EstimatedBytes` so you can observe the measured byte total of each exported batch.

## Documentation

Full documentation: **<https://elastic.github.io/elastic-ingest-dotnet/>**

- [Architecture](https://elastic.github.io/elastic-ingest-dotnet/architecture/) — how the two-stage buffered pipeline works
- [Channels](https://elastic.github.io/elastic-ingest-dotnet/channels/) — buffer tuning, callbacks, serialization
