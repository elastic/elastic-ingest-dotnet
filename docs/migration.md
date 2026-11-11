---
navigation_title: Migrating to 1.0
---

# Migrating to 1.0

The channels were rebuilt around explicit states, and the defects that came out of that are fixed. Most code compiles unchanged. These are the changes that can matter, in the order they will bite.

## Behaviour

**An export that throws is not retried by default.** An exception that escapes `ExportAsync` (an unexpected failure, a cancellation, a document that cannot be serialized) used to drop the batch silently on every attempt but the last. The items are now reported on the first exception, through `ExportExceptionCallback` and `ExportMaxRetriesCallback`, and are not sent again, because a retry after an unknown failure can index a document twice when it has no id. To retry the exceptions that are safe for your data, set the new `BufferOptions.ExportRetryOnException`:

```csharp
options.BufferOptions.ExportRetryOnException = e => e is TimeoutException;
```

**`WaitForDrainAsync` answers `true` only when nothing is left.** It used to return `true` while batches still waited behind a running export. It is also truly asynchronous now: it used to block the calling thread for up to the wait before returning its task. Without a `maxWait` the limit is `DrainRequestTimeout` times the exports still to run.

**A whole request answered with 429 is retried.** The channel used to drop the batch. When a page is split by `OutboundBufferMaxBytes`, only the sub-batch that failed is retried or rejected, and the results of the others stay with their documents. A sub-batch that got no answer at all is reported as rejected, not retried.

**`CompleteAsync` returns `false` when a channel did not drain.** Both sync orchestrators used to go on, deleting old documents or switching aliases, whatever the drain said. `DrainAllAsync` now returns whether both channels drained.

**`StartAsync` runs once.** A second call throws, and a start that failed leaves an orchestrator that says so instead of a `NullReferenceException`.

**`TryComplete(error)` ends the channel.** The inbound loop used to spin at 100% CPU.

## Source

- Parameter names changed to match what they override: `ctx` is `cancellationToken` on `WaitToWriteAsync(CancellationToken)`, `events` is `currentBuffer` and `consumedBufferStatistics` is `statistics` on `RetryBuffer`, and the `@event` of `RetryEvent` and `RejectEvent` is `item`. Positional calls are not affected; a call that names the parameter is.
- `IncrementalSyncOrchestrator.DrainAllAsync` returns `Task<bool>` instead of `Task`.
- `BufferOptions.TimeProvider` is new (default `TimeProvider.System`). It is the clock of the channel: the age of a buffer, the flush timer, the retry backoff, the wait of `WaitToWriteAsync` and the limit of `WaitForDrainAsync`. A test passes a `FakeTimeProvider`. The `netstandard` targets of `Elastic.Channels` now depend on `Microsoft.Bcl.TimeProvider`.
- `WaitToReadResult` is removed: the inbound loop no longer reads through a delay and a result, it is woken by a timer.
- `ResponseItemsBufferedChannelBase` has a new protected virtual `AllItemsSucceeded(TResponse)`, `false` by default. Override it when your response carries a summary you can trust, and the channel skips matching items to events when nothing failed. `IngestChannelBase` does, from the `errors` flag of the bulk response.
- `Elastic.Channels` also targets `net8.0`, `net10.0` and `net11.0`, and the three libraries target `net11.0`. The minimum version of the build is the .NET 11 RC SDK.

## Timing

A buffer that is not full is flushed once its lifetime has elapsed (its age is at least `OutboundBufferMaxLifetime`), where it used to need to be strictly older. Its age is measured on the monotonic clock of the time provider, so a wall clock that is set back no longer delays a flush.
