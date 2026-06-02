// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using Elastic.Channels;
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
}
