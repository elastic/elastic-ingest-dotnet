# Elastic.Channels

Provides an specialized `System.Threading.Channels.ChannelWriter` implementation that makes it easy
to consume data pushed to that thread in batches.

The batches will emit either when a certain maximum is hit or when a batch's lifecycle exceeds a certain age.

This allows data of various rates to pushed in the same manner while different implementations to send the batched data to receivers can be implemented. 

This package serves mainly as a core library with abstract classes 
and does not ship any useful implementations.

It ships with a `NoopBufferedChannel` implementation that does nothing in its `ExporExport implementation for unit test and benchmark purposes.


## BufferedChannelBase<>

An abstract class that requires implementers to implement:

```csharp
protected abstract Task<TResponse> Export(IReadOnlyCollection<TEvent> buffer, CancellationToken ctx);
```

Any implementation allows data to pushed to it through:

```csharp
var e = new TEvent();
if (await channel.WaitToWriteAsync(e))
	written++;
```

## ChannelOptionsBase<>

Implementers of `BufferedChannelBase<>` must also create their own implementation of `ChannelOptionsBase<>`. This to ensure each channel implementation creates an appropriately named options class.


## Quick minimal implementation

```chsarp

public class Event { }
public class Response { }

public class NoopChannelOptions 
  : ChannelOptionsBase<Event, Response> { }

public class NoopBufferedChannel 
  : BufferedChannelBase<NoopChannelOptions, Event, Response>
{

  public NoopBufferedChannel(NoopChannelOptions options) 
    : base(options) { }

  protected override Task<Response> Export(IReadOnlyCollection<NoopEvent> buffer, CancellationToken ctx)
  {
    return Task.FromResult(new Response());
  }
}
```

Now once we instantiate an `NoopBufferedChannel` we can use it push data to it.

```csharp
var e = new Event();
if (await noopChannel.WaitToWriteAsync(e))
	written++;
```

Both `NoopBufferedChannel` and a more specialized `DiagnosticsBufferedChannel` exist for test or debugging purposes.

`DiagnosticsBufferedChannel.ToString()` uncovers a lot of insights into the state machinery. 


## BufferOptions

Each `ChannelOptionsBase<>` implementation takes and exposes a `BufferOptions` instance. This controls the buffering behavior of `BufferedChannelBase<>`.


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
