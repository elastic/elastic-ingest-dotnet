// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Threading;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests;

/// <summary>
/// Tests for <see cref="BufferOptions.OutboundBufferMaxBytes"/> sub-batch slicing.
/// When the budget is set, <see cref="IngestChannelBase{TDocument,TChannelOptions}.ExportAsync"/>
/// splits one outbound page into multiple <c>_bulk</c> HTTP requests bounded by the budget,
/// serializing each event exactly once (no double-serialize).
/// </summary>
public class SubBatchingTests
{
	// ── tests ─────────────────────────────────────────────────────────────────

	/// <summary>
	/// Without a byte budget, all events in one outbound page are sent in a single streaming _bulk request.
	/// </summary>
	[Test]
	public void NoSubBatchingWithoutBudget()
	{
		// 3-item success response: if exactly 1 HTTP call is made, Zip aligns all 3 → no rejections.
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201, 201, 201))
		);

		var exportAttempts = 0;
		var totalItems = 0;
		var rejections = 0;

		var wh = new CountdownEvent(1);
		var bo = new BufferOptions
		{
			ExportMaxConcurrency = 1,
			OutboundBufferMaxSize = 3,
			OutboundBufferMaxLifetime = TimeSpan.FromSeconds(30),
			WaitHandle = wh,
			ExportMaxRetries = 0,
		};
		var opts = new IndexChannelOptions<TestDocument>(client)
		{
			BufferOptions = bo,
			ExportItemsAttemptCallback = (_, _) => Interlocked.Increment(ref exportAttempts),
			ExportResponseCallback = (r, _) => Interlocked.Add(ref totalItems, (r as BulkResponse).Items?.Count ?? 0),
			ServerRejectionCallback = _ => Interlocked.Increment(ref rejections),
		};
		using var channel = new IndexChannel<TestDocument>(opts);

		for (var i = 0; i < 3; i++) channel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		wh.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

		exportAttempts.Should().Be(1, "all 3 events go in a single _bulk call — one ExportAsync attempt");
		totalItems.Should().Be(3, "the single 3-item mock response covers all events");
		rejections.Should().Be(0);
	}

	/// <summary>
	/// With a budget smaller than any single event, each event becomes its own sub-batch.
	/// The N 1-item responses are merged into one BulkResponse with N items.
	/// The base retry loop still sees only one ExportAsync call.
	/// </summary>
	[Test]
	public void EachEventGetsOwnSubBatchWhenBudgetTiny()
	{
		// Three sequential 1-item rules. If 3 HTTP calls are made the merged response has 3 items.
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201))
			.ClientCalls(c => c.BulkResponse(201))
			.ClientCalls(c => c.BulkResponse(201))
		);

		var exportAttempts = 0;
		var totalItems = 0;
		var rejections = 0;

		var wh = new CountdownEvent(1);
		var bo = new BufferOptions
		{
			ExportMaxConcurrency = 1,
			OutboundBufferMaxSize = 3,
			OutboundBufferMaxLifetime = TimeSpan.FromSeconds(30),
			WaitHandle = wh,
			ExportMaxRetries = 0,
			OutboundBufferMaxBytes = 1, // every ~100-byte event exceeds 1 byte → own sub-batch
		};
		var opts = new IndexChannelOptions<TestDocument>(client)
		{
			BufferOptions = bo,
			ExportItemsAttemptCallback = (_, _) => Interlocked.Increment(ref exportAttempts),
			ExportResponseCallback = (r, _) => Interlocked.Add(ref totalItems, (r as BulkResponse).Items?.Count ?? 0),
			ServerRejectionCallback = _ => Interlocked.Increment(ref rejections),
		};
		using var channel = new IndexChannel<TestDocument>(opts);

		for (var i = 0; i < 3; i++) channel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		wh.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

		exportAttempts.Should().Be(1, "the base retry loop issues one ExportAsync call per outbound buffer");
		totalItems.Should().Be(3, "3 sub-batch responses (1 item each) merge to 3 total items");
		rejections.Should().Be(0, "no events dropped");
	}

	/// <summary>
	/// A single event that individually exceeds OutboundBufferMaxBytes triggers
	/// ItemExceedsBytesBudgetCallback but is still exported in its own sub-batch.
	/// </summary>
	[Test]
	public void OversizedSingleEventFiresCallbackAndIsExported()
	{
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201))
		);

		var oversizedCount = 0;
		var totalItems = 0;

		var wh = new CountdownEvent(1);
		var bo = new BufferOptions
		{
			ExportMaxConcurrency = 1,
			OutboundBufferMaxSize = 1,
			OutboundBufferMaxLifetime = TimeSpan.FromSeconds(30),
			WaitHandle = wh,
			ExportMaxRetries = 0,
			OutboundBufferMaxBytes = 1,  // real events are ~100 bytes → all "oversized"
		};
		var opts = new IndexChannelOptions<TestDocument>(client)
		{
			BufferOptions = bo,
			ItemExceedsBytesBudgetCallback = (_, _) => Interlocked.Increment(ref oversizedCount),
			ExportResponseCallback = (r, _) => Interlocked.Add(ref totalItems, (r as BulkResponse).Items?.Count ?? 0),
		};
		using var channel = new IndexChannel<TestDocument>(opts);

		channel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		wh.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

		oversizedCount.Should().Be(1, "the callback must fire for the event that exceeds the budget");
		totalItems.Should().Be(1, "the oversized event must still be exported");
	}

	/// <summary>
	/// A 429 on any sub-batch causes the merged response to carry a 429 status, which triggers
	/// RetryAllItems. All events in the original buffer are retried and eventually succeed.
	/// </summary>
	[Test]
	public void RetryTriggeredWhenAnySubBatchReturns429()
	{
		// 2 events, budget=1 → 2 sub-batch HTTP calls per ExportAsync invocation.
		// Attempt 1: call1→201, call2→429. MergeSubBatchResponses picks the 429 as carrier → RetryAllItems.
		// Attempt 2: call3→201, call4→201. Merged → success.
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201))   // attempt 1, sub-batch 1
			.ClientCalls(c => c.BulkResponse(429))   // attempt 1, sub-batch 2
			.ClientCalls(c => c.BulkResponse(201))   // attempt 2 (retry), sub-batch 1
			.ClientCalls(c => c.BulkResponse(201))   // attempt 2 (retry), sub-batch 2
		);

		var retries = 0;
		var rejections = 0;

		var wh = new CountdownEvent(1);
		var bo = new BufferOptions
		{
			ExportMaxConcurrency = 1,
			OutboundBufferMaxSize = 2,
			OutboundBufferMaxLifetime = TimeSpan.FromSeconds(30),
			WaitHandle = wh,
			ExportMaxRetries = 3,
			ExportBackoffPeriod = _ => TimeSpan.FromMilliseconds(1),
			OutboundBufferMaxBytes = 1,
		};
		var opts = new IndexChannelOptions<TestDocument>(client)
		{
			BufferOptions = bo,
			ServerRejectionCallback = _ => Interlocked.Increment(ref rejections),
			ExportRetryCallback = _ => Interlocked.Increment(ref retries),
		};
		using var channel = new IndexChannel<TestDocument>(opts);

		channel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		channel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		wh.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

		retries.Should().Be(1, "the 429 sub-batch triggers exactly one retry of all events in the buffer");
		rejections.Should().Be(0, "all events succeed after the retry");
	}

	/// <summary>
	/// Events pack into sub-batches bounded by OutboundBufferMaxBytes.
	/// With a budget that fits exactly 1 event (~100 bytes each), 2 events → 2 sub-batches.
	/// </summary>
	[Test]
	public void EventsPackIntoSubBatchesBoundedByBudget()
	{
		// 2 sub-batch calls, each returns 1-item success. Merged: 2 items. No rejections.
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201))
			.ClientCalls(c => c.BulkResponse(201))
		);

		var totalItems = 0;
		var rejections = 0;

		var wh = new CountdownEvent(1);
		var bo = new BufferOptions
		{
			ExportMaxConcurrency = 1,
			OutboundBufferMaxSize = 2,
			OutboundBufferMaxLifetime = TimeSpan.FromSeconds(30),
			WaitHandle = wh,
			ExportMaxRetries = 0,
			// A TestDocument serializes to ~100 bytes of NDJSON (action header + source).
			// 150-byte budget: 1 event fits, 2 events don't → 1 event per sub-batch.
			OutboundBufferMaxBytes = 150,
		};
		var opts = new IndexChannelOptions<TestDocument>(client)
		{
			BufferOptions = bo,
			ServerRejectionCallback = _ => Interlocked.Increment(ref rejections),
			ExportResponseCallback = (r, _) => Interlocked.Add(ref totalItems, (r as BulkResponse).Items?.Count ?? 0),
		};
		using var channel = new IndexChannel<TestDocument>(opts);

		channel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		channel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		wh.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

		totalItems.Should().Be(2, "2 sub-batches (1 item each) merge to 2 total items");
		rejections.Should().Be(0);
	}

	/// <summary>
	/// The ExportItemsAttemptCallback fires once per outbound buffer, not per sub-batch HTTP request.
	/// Sub-batching is fully transparent to the base retry loop.
	/// </summary>
	[Test]
	public void ExportAttemptCountIsOnePerBufferRegardlessOfSubBatchCount()
	{
		// 4 events, budget=1 → 4 sub-batch HTTP calls within 1 ExportAsync invocation.
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201))
			.ClientCalls(c => c.BulkResponse(201))
			.ClientCalls(c => c.BulkResponse(201))
			.ClientCalls(c => c.BulkResponse(201))
		);

		var exportAttempts = 0;
		var totalItems = 0;

		var wh = new CountdownEvent(1);
		var bo = new BufferOptions
		{
			ExportMaxConcurrency = 1,
			OutboundBufferMaxSize = 4,
			OutboundBufferMaxLifetime = TimeSpan.FromSeconds(30),
			WaitHandle = wh,
			ExportMaxRetries = 0,
			OutboundBufferMaxBytes = 1,
		};
		var opts = new IndexChannelOptions<TestDocument>(client)
		{
			BufferOptions = bo,
			ExportItemsAttemptCallback = (_, _) => Interlocked.Increment(ref exportAttempts),
			ExportResponseCallback = (r, _) => Interlocked.Add(ref totalItems, (r as BulkResponse).Items?.Count ?? 0),
		};
		using var channel = new IndexChannel<TestDocument>(opts);

		for (var i = 0; i < 4; i++) channel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		wh.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

		exportAttempts.Should().Be(1,
			"the retry loop issues one ExportAsync call per buffer regardless of how many sub-batch HTTP requests it issues internally");
		totalItems.Should().Be(4, "4 sub-batch responses (1 item each) merge to 4 total items");
	}

	/// <summary>
	/// The merged Items list preserves page order across sub-batch boundaries.
	/// A 400-status item in the last sub-batch must be correctly aligned to the last event
	/// in the original page, not to any event from an earlier sub-batch.
	/// </summary>
	[Test]
	public void MixedItemStatusesPreserveZipAlignmentAcrossSubBatches()
	{
		// 4 events, budget=1 → one event per sub-batch.
		// Sub-batches 1–3 succeed; sub-batch 4 returns a 400 rejection.
		// The merged Items array = [201, 201, 201, 400] in page order.
		// Zip pairs page[3] (last event) with the 400 item → only that event is rejected.
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201))
			.ClientCalls(c => c.BulkResponse(201))
			.ClientCalls(c => c.BulkResponse(201))
			.ClientCalls(c => c.BulkResponse(400))  // last event rejected
		);

		var origin = DateTimeOffset.UtcNow;
		List<(TestDocument, BulkResponseItem)> rejectedItems = null;
		var totalItems = 0;

		var wh = new CountdownEvent(1);
		var bo = new BufferOptions
		{
			ExportMaxConcurrency = 1,
			OutboundBufferMaxSize = 4,
			OutboundBufferMaxLifetime = TimeSpan.FromSeconds(30),
			WaitHandle = wh,
			ExportMaxRetries = 0,
			OutboundBufferMaxBytes = 1,
		};
		var opts = new IndexChannelOptions<TestDocument>(client)
		{
			BufferOptions = bo,
			ExportResponseCallback = (r, _) => Interlocked.Add(ref totalItems, (r as BulkResponse).Items?.Count ?? 0),
			ServerRejectionCallback = list => rejectedItems = list,
		};
		using var channel = new IndexChannel<TestDocument>(opts);

		// Write 4 events with distinct, verifiable timestamps.
		for (var i = 0; i < 4; i++)
			channel.TryWrite(new TestDocument { Timestamp = origin.AddSeconds(i) });

		wh.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

		totalItems.Should().Be(4, "all 4 sub-batch items must be present in the merged response");
		rejectedItems.Should().NotBeNull().And.HaveCount(1, "only the 4th event was rejected");
		rejectedItems[0].Item1.Timestamp.Should().Be(origin.AddSeconds(3),
			"the rejected event must be the 4th one (last sub-batch), not one from an earlier sub-batch — proves Zip alignment is correct after merge");
		rejectedItems[0].Item2.Status.Should().Be(400);
	}

	/// <summary>
	/// DataStreamChannel inherits the same ExportAsync override as IndexChannel.
	/// Verify it also slices outbound pages into sub-batches bounded by the byte budget.
	/// </summary>
	[Test]
	public void DataStreamChannelAlsoSubBatches()
	{
		// 3 events, budget=1 → 3 sub-batches. Uses DataStreamChannel, not IndexChannel.
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201))
			.ClientCalls(c => c.BulkResponse(201))
			.ClientCalls(c => c.BulkResponse(201))
		);

		var totalItems = 0;
		var rejections = 0;

		var wh = new CountdownEvent(1);
		var bo = new BufferOptions
		{
			ExportMaxConcurrency = 1,
			OutboundBufferMaxSize = 3,
			OutboundBufferMaxLifetime = TimeSpan.FromSeconds(30),
			WaitHandle = wh,
			ExportMaxRetries = 0,
			OutboundBufferMaxBytes = 1,
		};
		var opts = new DataStreamChannelOptions<TestDocument>(client)
		{
			BufferOptions = bo,
			DataStream = new DataStreamName("logs"),
			ExportResponseCallback = (r, _) => Interlocked.Add(ref totalItems, (r as BulkResponse).Items?.Count ?? 0),
			ServerRejectionCallback = _ => Interlocked.Increment(ref rejections),
		};
		using var channel = new DataStreamChannel<TestDocument>(opts);

		for (var i = 0; i < 3; i++)
			channel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });

		wh.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

		totalItems.Should().Be(3, "DataStreamChannel must produce 3 merged sub-batch items");
		rejections.Should().Be(0);
	}

	/// <summary>
	/// Multiple consecutive buffer flushes each sub-batch independently.
	/// The MemoryStream buffers are local to each ExportAsync call so there is no
	/// state bleed from one flush into the next.
	/// </summary>
	[Test]
	public void ConsecutiveFlushesSubBatchIndependently()
	{
		// Two rounds of 2 events each. Budget=1 → 2 sub-batches per round → 4 HTTP calls total.
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201))  // round 1, sub-batch 1
			.ClientCalls(c => c.BulkResponse(201))  // round 1, sub-batch 2
			.ClientCalls(c => c.BulkResponse(201))  // round 2, sub-batch 1
			.ClientCalls(c => c.BulkResponse(201))  // round 2, sub-batch 2
		);

		var totalItems = 0;

		var wh = new CountdownEvent(1);
		var bo = new BufferOptions
		{
			ExportMaxConcurrency = 1,
			OutboundBufferMaxSize = 2,
			OutboundBufferMaxLifetime = TimeSpan.FromSeconds(30),
			WaitHandle = wh,
			ExportMaxRetries = 0,
			OutboundBufferMaxBytes = 1,
		};
		var opts = new IndexChannelOptions<TestDocument>(client)
		{
			BufferOptions = bo,
			ExportResponseCallback = (r, _) => Interlocked.Add(ref totalItems, (r as BulkResponse).Items?.Count ?? 0),
		};
		using var channel = new IndexChannel<TestDocument>(opts);

		// Round 1
		channel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		channel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		wh.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("round 1 should drain");

		// Round 2
		wh.Reset();
		channel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		channel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		wh.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("round 2 should drain");

		totalItems.Should().Be(4, "both rounds must produce correct merged responses: 2 sub-batches × 2 rounds × 1 item each");
	}

	/// <summary>
	/// Validates that the actual serialized NDJSON size of a TestDocument is within the range
	/// that makes the budget-split tests meaningful, and confirms the byte boundary logic fires
	/// at the right threshold.
	/// </summary>
	[Test]
	public void ActualEventByteSizeIsWithinExpectedRange()
	{
		// First: measure the real per-event NDJSON byte size by sending 1 event with debug mode on.
		// EnableDebugMode() (set in TestSetup.CreateClient) causes RequestBodyInBytes to be populated.
		int actualBytesPerEvent = 0;
		var measureWh = new CountdownEvent(1);

		var measureClient = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201))
		);
		var measureOpts = new IndexChannelOptions<TestDocument>(measureClient)
		{
			BufferOptions = new BufferOptions
			{
				OutboundBufferMaxSize = 1,
				OutboundBufferMaxLifetime = TimeSpan.FromSeconds(30),
				WaitHandle = measureWh,
				ExportMaxRetries = 0,
			},
			ExportResponseCallback = (r, _) =>
				actualBytesPerEvent = (r as BulkResponse).ApiCallDetails.RequestBodyInBytes?.Length ?? 0,
		};
		using (var measureChannel = new IndexChannel<TestDocument>(measureOpts))
		{
			measureChannel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
			measureWh.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();
		}

		actualBytesPerEvent.Should().BeGreaterThan(50, "a minimal NDJSON event must include the action header and source");
		actualBytesPerEvent.Should().BeLessThan(500, "a simple TestDocument should not produce an unexpectedly large payload");

		// Now validate: with budget set between 1× and 2× the actual event size, two events
		// must split into 2 sub-batches. This confirms the 150-byte assumption in
		// EventsPackIntoSubBatchesBoundedByBudget is valid, even if TestDocument changes.
		var budgetThatFitsOneButNotTwo = (long)(actualBytesPerEvent * 1.5);

		var splitClient = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201))
			.ClientCalls(c => c.BulkResponse(201))
		);
		var totalItems = 0;
		var splitWh = new CountdownEvent(1);
		var splitOpts = new IndexChannelOptions<TestDocument>(splitClient)
		{
			BufferOptions = new BufferOptions
			{
				OutboundBufferMaxSize = 2,
				OutboundBufferMaxLifetime = TimeSpan.FromSeconds(30),
				WaitHandle = splitWh,
				ExportMaxRetries = 0,
				OutboundBufferMaxBytes = budgetThatFitsOneButNotTwo,
			},
			ExportResponseCallback = (r, _) => Interlocked.Add(ref totalItems, (r as BulkResponse).Items?.Count ?? 0),
		};
		using var splitChannel = new IndexChannel<TestDocument>(splitOpts);

		splitChannel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		splitChannel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		splitWh.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

		totalItems.Should().Be(2,
			$"with budget={budgetThatFitsOneButNotTwo} bytes (1.5× actual event size of {actualBytesPerEvent} bytes), " +
			$"2 events must produce 2 sub-batches (1 item each)");
	}
}
