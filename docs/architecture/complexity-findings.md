---
navigation_title: Complexity findings
---

# Where the complexity is

Notes from modernising the channels: what was measured, what was changed because of it, and what was left alone on purpose. The decisions at the end are for the owners.

## What the numbers said

`ChannelBenchmarks` runs 100,000 events through a channel with a no-op export, diagnostics off. Allocated bytes, BenchmarkDotNet short run (the time column is too noisy at that length to quote):

| | `origin/main` | now |
|---|---|---|
| one worker | 18.7 MB | 3.8 MB |
| four workers | 84.8 MB | 9.0 MB |
| writers that wait for room | 84.8 MB | 9.4 MB |

Two things account for it. Every inbound read allocated a `Task.Delay`, a `Task.WhenAny` and an `AsTask`, and left the delay's timer running until it expired. And the old drain polled in 100 ms steps. `RetryBufferBenchmarks` shows the response step for one 5,000 document export: 263,213 B and about 55 µs before, 0 B and 3 ns after when the response says `errors: false`.

The end to end `BulkIngestionBenchmarks` could not resolve changes of this size: their allocation varies by tens of MB between runs of the same code. Micro-benchmarks that call the step directly did.

## What was left alone, and why

- **Sub-batching streams.** `ExportWithSubBatchingAsync` uses two `MemoryStream`s per page, and only when `OutboundBufferMaxBytes` is set. No benchmark shows it among the costs, so it is unchanged.
- **`_taskList`, `_throttleExportTasks`, `LongRunning`.** Small, per channel rather than per event. A pool would add a dependency and, as in `Elastic.Transport`, pooled buffers in memory dumps read as leaks.
- **Synchronous continuations.** Both channels allow them, so completing the inbound channel runs the consumer on the calling thread until its first real `await`. An export that blocks synchronously before awaiting would block the caller of `TryComplete`. Real exports await the transport at once. Switching it off costs a thread hop per wake, so it needs its own measurement.
- **`Zip` returns a `List` of tuples.** It is a protected abstract member, so changing it breaks every subclass. The fast path means it is called only when a response reports errors.
- **Retrying exceptions.** An exception that escapes `ExportAsync` is not retried by default, see the migration notes. The predicate is an opt-in.

## What is still large

`BufferedChannelBase` is 900 lines. It grew because the state is explicit now and carries its documentation; the loop and the lifecycle are each short. `IncrementalSyncOrchestrator` and `DeltaSyncOrchestrator` (650 lines each) repeat most of their start-up, drain and completion code. They share `OrchestratorLifecycle` now, and could share more.

## Decisions for the owners

1. **`Elastic.Transport` 1.1.0.** The repository is on 0.17.3, while 1.0.0 and 1.1.0 are on nuget.org. Moving is a separate change.
2. **CI runs two of eight test projects.** `Elastic.Mapping.Tests` and `Elastic.Clients.AgentBuilder.Tests` are unit tests and are not run in CI.
3. **Merging the two orchestrators' shared code** into one base, now that their lifecycle is the same.
4. **The open items of `AI_ENRICHMENT_REVIEW.md`**, an orphan-cleanup ID mismatch and fragile JSON splicing, are a separate feature area and are untouched.
5. **`netstandard2.0` and `netstandard2.1`** are still targeted. Dropping them would let the polyfills and the `#if` branches go.
