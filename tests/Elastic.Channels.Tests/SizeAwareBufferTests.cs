// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Channels.Tests;

public class SizeAwareBufferTests
{
	/// <summary>
	/// A channel whose events carry an explicit byte size (set by the test).
	/// Records every exported batch for post-hoc assertions, and tracks how many times
	/// <see cref="CalculateOutboundBytesAsync"/> is invoked (to verify it is skipped when the budget is null).
	/// </summary>
	private sealed class SizedEventChannel : BufferedChannelBase<SizedEventChannel.ChannelOptions, SizedEventChannel.Event, SizedEventChannel.Response>
	{
		public sealed class Event
		{
			public long SizeInBytes { get; set; }
		}

		public sealed class Response { }

		public sealed class ChannelOptions : ChannelOptionsBase<Event, Response> { }

		// Thread-safe: ExportAsync may be called concurrently from multiple export tasks.
		private readonly ConcurrentBag<Event[]> _exportedBatches = new();

		private int _calculateCallCount;

		public IReadOnlyCollection<Event[]> ExportedBatches => _exportedBatches;

		/// <summary> Total number of events across all exported batches. </summary>
		public int TotalExported => _exportedBatches.Sum(b => b.Length);

		/// <summary> Number of times <see cref="CalculateOutboundBytesAsync"/> was invoked. </summary>
		public int CalculateCallCount => _calculateCallCount;

		public SizedEventChannel(BufferOptions bufferOptions) :
			base(new ChannelOptions { BufferOptions = bufferOptions }) { }

		public SizedEventChannel(ChannelOptions options) : base(options) { }

		protected override ValueTask<long> CalculateOutboundBytesAsync(Event @event, CancellationToken ctx = default)
		{
			Interlocked.Increment(ref _calculateCallCount);
			return new(@event.SizeInBytes);
		}

		protected override Task<Response> ExportAsync(ArraySegment<Event> buffer, CancellationToken ctx = default)
		{
			_exportedBatches.Add(buffer.ToArray());
			return Task.FromResult(new Response());
		}
	}

	// ── helpers ──────────────────────────────────────────────────────────────

	/// <summary>
	/// Asserts the per-batch byte invariant: every batch's total estimated bytes must not exceed
	/// <paramref name="byteBudget"/> unless the batch is a single item that individually exceeds it
	/// (best-effort — oversized items always get their own batch).
	/// </summary>
	private static void AssertByteBudgetRespected(SizedEventChannel.Event[][] batches, long byteBudget)
	{
		foreach (var batch in batches)
		{
			var batchBytes = batch.Sum(e => e.SizeInBytes);
			var isSingleOversized = batch.Length == 1 && batch[0].SizeInBytes > byteBudget;
			if (!isSingleOversized)
				batchBytes.Should().BeLessOrEqualTo(byteBudget,
					$"batch of {batch.Length} item(s) with total {batchBytes} bytes exceeded the {byteBudget}-byte budget");
		}
	}

	// ── core flush behaviour ─────────────────────────────────────────────────

	[Test]
	public async Task BytesBudgetTriggersFlushBeforeCountLimit()
	{
		// 400-byte events, budget 1000, count limit 100.
		// After 2 events (800 bytes), average is 400.
		// Before adding a 3rd: 800 + max(400, avg=400) = 1200 > 1000 → pre-flush.
		// So each batch holds exactly 2 events (800 bytes), the count limit is never reached.
		const long byteBudget = 1000;
		const int totalEvents = 20;

		using var channel = new SizedEventChannel(new BufferOptions
		{
			OutboundBufferMaxSize = 100,
			OutboundBufferMaxBytes = byteBudget,
		});

		for (var i = 0; i < totalEvents; i++)
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 400 });

		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(5));

		var batches = channel.ExportedBatches.ToArray();
		AssertByteBudgetRespected(batches, byteBudget);
		batches.Sum(b => b.Length).Should().Be(totalEvents, "no events should be dropped");
		batches.Should().AllSatisfy(b => b.Length.Should().BeLessOrEqualTo(2),
			"the byte budget should prevent batches larger than 2 × 400-byte events");
	}

	[Test]
	public async Task CountLimitTriggersFlushBeforeByteBudget()
	{
		// Tiny events (1 byte each), huge byte budget — count fires every time.
		const int countBudget = 5;
		const int totalEvents = 25;
		var expectedBatches = totalEvents / countBudget;

		var bufferOptions = new BufferOptions
		{
			OutboundBufferMaxSize = countBudget,
			OutboundBufferMaxBytes = 1_000_000,
			WaitHandle = new CountdownEvent(expectedBatches),
		};
		using var channel = new SizedEventChannel(bufferOptions);

		for (var i = 0; i < totalEvents; i++)
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 1 });

		bufferOptions.WaitHandle.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

		var batches = channel.ExportedBatches.ToArray();
		batches.Should().HaveCount(expectedBatches);
		batches.All(b => b.Length == countBudget).Should().BeTrue("count limit should be the binding constraint");
	}

	[Test]
	public async Task NullBudgetPacksToCountLimitOnly()
	{
		// No byte budget → events pack entirely by count, even when events are "huge".
		const int countBudget = 10;
		const int totalEvents = 50;
		var expectedBatches = totalEvents / countBudget;

		var bufferOptions = new BufferOptions
		{
			OutboundBufferMaxSize = countBudget,
			OutboundBufferMaxBytes = null,
			WaitHandle = new CountdownEvent(expectedBatches),
		};
		using var channel = new SizedEventChannel(bufferOptions);

		for (var i = 0; i < totalEvents; i++)
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 999_999 });

		bufferOptions.WaitHandle.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

		var batches = channel.ExportedBatches.ToArray();
		batches.Should().HaveCount(expectedBatches);
		batches.All(b => b.Length == countBudget).Should().BeTrue("each batch should be exactly the count limit");
	}

	[Test]
	public async Task NullBudgetCalculateIsNeverCalled()
	{
		// When OutboundBufferMaxBytes is null, CalculateOutboundBytesAsync must never be invoked —
		// no virtual dispatch, no allocation, zero overhead.
		const int countBudget = 5;
		var bufferOptions = new BufferOptions
		{
			OutboundBufferMaxSize = countBudget,
			OutboundBufferMaxBytes = null,
			WaitHandle = new CountdownEvent(2),
		};
		using var channel = new SizedEventChannel(bufferOptions);

		for (var i = 0; i < 10; i++)
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 999_999 });

		bufferOptions.WaitHandle.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

		channel.CalculateCallCount.Should().Be(0, "CalculateOutboundBytesAsync must not be called when the byte budget is disabled");
	}

	// ── oversized items ──────────────────────────────────────────────────────

	[Test]
	public async Task SingleOversizedFirstItemGetsSoloBatchAndFiresCallback()
	{
		// When the very first item in an empty buffer exceeds the budget: no pre-flush is possible
		// (nothing to flush), the item is added and the byte safety-net fires, resulting in a
		// single-item batch. The callback is fired.
		const long budget = 100;
		var callbackItems = new ConcurrentBag<long>();

		var options = new SizedEventChannel.ChannelOptions
		{
			BufferOptions = new BufferOptions
			{
				OutboundBufferMaxSize = 100,
				OutboundBufferMaxBytes = budget,
			},
			ItemExceedsBytesBudgetCallback = (_, bytes) => callbackItems.Add(bytes),
		};
		using var channel = new SizedEventChannel(options);

		await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 500 });
		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(5));

		var batches = channel.ExportedBatches.ToArray();
		batches.Should().HaveCount(1, "one oversized item → one batch");
		batches[0].Should().HaveCount(1, "the batch should contain only that one item");
		callbackItems.Should().ContainSingle(b => b == 500, "callback must fire with the item's measured size");
	}

	[Test]
	public async Task OversizedItemMidStreamFlushesAccumulatedFirstThenAddsOversized()
	{
		// Small items accumulate, then a large item arrives.
		// The large item triggers a pre-flush of accumulated items first, then lands in its own batch.
		const long budget = 500;
		var callbackBytes = new ConcurrentBag<long>();

		var options = new SizedEventChannel.ChannelOptions
		{
			BufferOptions = new BufferOptions
			{
				OutboundBufferMaxSize = 100,
				OutboundBufferMaxBytes = budget,
			},
			ItemExceedsBytesBudgetCallback = (_, bytes) => callbackBytes.Add(bytes),
		};
		using var channel = new SizedEventChannel(options);

		// 4 small items (100 bytes each → 400 total) then 1 oversized (600 bytes).
		for (var i = 0; i < 4; i++)
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 100 });
		await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 600 });

		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(5));

		var batches = channel.ExportedBatches.ToArray();
		channel.TotalExported.Should().Be(5, "all 5 events must be exported");
		AssertByteBudgetRespected(batches, budget);

		// The oversized item must appear in a solo batch.
		batches.Should().Contain(b => b.Length == 1 && b[0].SizeInBytes == 600,
			"the oversized item must be isolated in its own batch");
		callbackBytes.Should().ContainSingle(b => b == 600);
	}

	[Test]
	public async Task AllOversizedItemsEachGetOwnBatch()
	{
		// Every event individually exceeds the budget → each goes to a solo batch.
		const long budget = 100;
		const int totalEvents = 10;
		var callbackCount = 0;

		var options = new SizedEventChannel.ChannelOptions
		{
			BufferOptions = new BufferOptions
			{
				OutboundBufferMaxSize = 100,
				OutboundBufferMaxBytes = budget,
			},
			ItemExceedsBytesBudgetCallback = (_, _) => Interlocked.Increment(ref callbackCount),
		};
		using var channel = new SizedEventChannel(options);

		for (var i = 0; i < totalEvents; i++)
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 500 });

		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(5));

		channel.TotalExported.Should().Be(totalEvents, "all oversized events must still be exported");
		channel.ExportedBatches.All(b => b.Length == 1).Should().BeTrue(
			"each oversized event must land in its own single-item batch");
		callbackCount.Should().Be(totalEvents, "callback must fire once per oversized item");
	}

	[Test]
	public async Task OversizedCallbackNotFiredWhenBudgetIsNull()
	{
		// With no byte budget configured, ItemExceedsBytesBudgetCallback must never fire,
		// regardless of how large the events claim to be.
		var callbackFired = false;
		var options = new SizedEventChannel.ChannelOptions
		{
			BufferOptions = new BufferOptions
			{
				OutboundBufferMaxSize = 5,
				OutboundBufferMaxBytes = null,
				WaitHandle = new CountdownEvent(2),
			},
			ItemExceedsBytesBudgetCallback = (_, _) => callbackFired = true,
		};
		using var channel = new SizedEventChannel(options);

		for (var i = 0; i < 10; i++)
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 999_999 });

		options.BufferOptions.WaitHandle!.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

		callbackFired.Should().BeFalse("the callback must never be invoked when OutboundBufferMaxBytes is null");
	}

	// ── boundary conditions ───────────────────────────────────────────────────

	[Test]
	public async Task ExactlyAtBudgetFlushesCorrectly()
	{
		// 3 events of 200 bytes each. Budget = 600 bytes.
		// All 3 fit exactly (600 ≤ 600 does not trigger WouldExceedBytes).
		// After the last item the byte safety-net fires (600 is NOT < 600 → ThresholdsHit = true).
		const long budget = 600;

		using var channel = new SizedEventChannel(new BufferOptions
		{
			OutboundBufferMaxSize = 100,
			OutboundBufferMaxBytes = budget,
		});

		for (var i = 0; i < 3; i++)
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 200 });

		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(5));

		var batches = channel.ExportedBatches.ToArray();
		channel.TotalExported.Should().Be(3);
		// All 3 items should be in one or two batches, none exceeding 600 bytes.
		AssertByteBudgetRespected(batches, budget);
	}

	[Test]
	public async Task VerySmallBudgetOneItemPerBatch()
	{
		// Budget = 1 byte; every 100-byte event exceeds it.
		// Each event should land in its own batch.
		const long budget = 1;
		const int totalEvents = 8;
		var callbackCount = 0;

		var options = new SizedEventChannel.ChannelOptions
		{
			BufferOptions = new BufferOptions
			{
				OutboundBufferMaxSize = 100,
				OutboundBufferMaxBytes = budget,
			},
			ItemExceedsBytesBudgetCallback = (_, _) => Interlocked.Increment(ref callbackCount),
		};
		using var channel = new SizedEventChannel(options);

		for (var i = 0; i < totalEvents; i++)
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 100 });

		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(5));

		channel.TotalExported.Should().Be(totalEvents);
		channel.ExportedBatches.All(b => b.Length == 1).Should().BeTrue(
			"every item exceeds the 1-byte budget and must get its own batch");
		callbackCount.Should().Be(totalEvents);
	}

	[Test]
	public async Task ZeroSizeItemsNeverTriggerByteBudgetAndPackByCount()
	{
		// Events that report 0 bytes should never trigger the byte budget — the running total
		// stays at 0, WouldExceedBytes is always false, and count is the only flush trigger.
		const int countBudget = 5;
		const int totalEvents = 25;
		var expectedBatches = totalEvents / countBudget;

		var bufferOptions = new BufferOptions
		{
			OutboundBufferMaxSize = countBudget,
			OutboundBufferMaxBytes = 1,       // tiny budget; zero-size items must NOT trip it
			WaitHandle = new CountdownEvent(expectedBatches),
		};
		using var channel = new SizedEventChannel(bufferOptions);

		for (var i = 0; i < totalEvents; i++)
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 0 });

		bufferOptions.WaitHandle.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

		var batches = channel.ExportedBatches.ToArray();
		batches.Should().HaveCount(expectedBatches, "count limit should be the only trigger for zero-byte events");
		batches.All(b => b.Length == countBudget).Should().BeTrue();
	}

	[Test]
	public async Task BudgetFarExceedsAllItemsProducesOneBatch()
	{
		// When the total byte size of all events is well below the budget AND the count is below the
		// count limit, no early flush occurs. After TryComplete the terminal flush collects them all.
		const long budget = 100_000;
		const int totalEvents = 10;

		using var channel = new SizedEventChannel(new BufferOptions
		{
			OutboundBufferMaxSize = 100,
			OutboundBufferMaxBytes = budget,
		});

		for (var i = 0; i < totalEvents; i++)
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 100 });

		channel.TryComplete();
		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(5));

		// All 10 events should come out in one batch (total 1000 bytes << 100,000 budget).
		channel.TotalExported.Should().Be(totalEvents);
		channel.ExportedBatches.Should().HaveCount(1, "no early flush should occur when byte total is far below the budget");
	}

	[Test]
	public async Task LifetimeFlushWithByteBudgetEnabled()
	{
		// A single event that is well below the byte budget AND below the count limit must still be
		// flushed by the terminal drain path (TryComplete → ConsumeInboundEventsAsync final flush).
		using var channel = new SizedEventChannel(new BufferOptions
		{
			OutboundBufferMaxSize = 100,
			OutboundBufferMaxBytes = 100_000,
		});

		await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 50 });

		channel.TryComplete();
		var drained = await channel.WaitForDrainAsync(TimeSpan.FromSeconds(5));

		drained.Should().BeTrue("the lone event must be flushed on drain");
		channel.TotalExported.Should().Be(1);
	}

	// ── estimated bytes on callbacks ─────────────────────────────────────────

	[Test]
	public async Task EstimatedBytesOnResponseCallbackMatchesEventSizes()
	{
		// The IWriteTrackingBuffer.EstimatedBytes passed to ExportResponseCallback must equal
		// the sum of SizeInBytes for the events in that batch.
		var observedEstimatedBytes = new ConcurrentBag<long>();

		const long budget = 600;
		var options = new SizedEventChannel.ChannelOptions
		{
			BufferOptions = new BufferOptions
			{
				OutboundBufferMaxSize = 100,
				OutboundBufferMaxBytes = budget,
			},
			ExportResponseCallback = (_, tracking) => observedEstimatedBytes.Add(tracking.EstimatedBytes),
		};
		using var channel = new SizedEventChannel(options);

		// 200-byte items: 3 per batch at budget 600 (200+200+200 = 600 ≤ 600 then WouldExceed on 4th)
		// Actually: after 2 (400) avg=200, WouldExceed(200)? 400+max(200,200)=600 ≤ 600 → no. After 3 (600):
		// WouldExceed(200)? 600+max(200,200)=800>600 → pre-flush. So batches of 3.
		for (var i = 0; i < 12; i++)
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 200 });

		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(5));

		observedEstimatedBytes.Should().NotBeEmpty();
		observedEstimatedBytes.All(b => b > 0 && b <= budget).Should().BeTrue(
			"EstimatedBytes per batch should be positive and within the budget");
	}

	[Test]
	public async Task EstimatedBytesIsZeroWhenBudgetDisabled()
	{
		// With no byte budget, CalculateOutboundBytesAsync is never called, so EstimatedBytes is 0.
		var observedEstimatedBytes = new ConcurrentBag<long>();

		var options = new SizedEventChannel.ChannelOptions
		{
			BufferOptions = new BufferOptions
			{
				OutboundBufferMaxSize = 5,
				OutboundBufferMaxBytes = null,
				WaitHandle = new CountdownEvent(2),
			},
			ExportResponseCallback = (_, tracking) => observedEstimatedBytes.Add(tracking.EstimatedBytes),
		};
		using var channel = new SizedEventChannel(options);

		for (var i = 0; i < 10; i++)
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 999 });

		options.BufferOptions.WaitHandle!.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();

		observedEstimatedBytes.Should().NotBeEmpty();
		observedEstimatedBytes.All(b => b == 0).Should().BeTrue(
			"EstimatedBytes must be 0 when OutboundBufferMaxBytes is null");
	}

	// ── no-drops invariant ───────────────────────────────────────────────────

	[Test]
	public async Task AllEventsExportedWithSmallUniformItems()
	{
		const int totalEvents = 200;
		const long budget = 1000;

		using var channel = new SizedEventChannel(new BufferOptions
		{
			OutboundBufferMaxSize = 100,
			OutboundBufferMaxBytes = budget,
		});

		for (var i = 0; i < totalEvents; i++)
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 50 });

		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(10));

		channel.TotalExported.Should().Be(totalEvents, "no events should be dropped");
		AssertByteBudgetRespected(channel.ExportedBatches.ToArray(), budget);
	}

	[Test]
	public async Task AllEventsExportedWithMixedSizes()
	{
		const int totalEvents = 100;
		const long budget = 1000;

		using var channel = new SizedEventChannel(new BufferOptions
		{
			OutboundBufferMaxSize = 100,
			OutboundBufferMaxBytes = budget,
		});

		for (var i = 0; i < totalEvents; i++)
		{
			// Mix: 1-byte, 300-byte, 600-byte, 1200-byte (oversized) in rotation.
			var size = (i % 4) switch { 0 => 1L, 1 => 300L, 2 => 600L, _ => 1200L };
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = size });
		}

		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(10));

		channel.TotalExported.Should().Be(totalEvents, "no events should be dropped for mixed sizes");
		AssertByteBudgetRespected(channel.ExportedBatches.ToArray(), budget);
	}

	[Test]
	public async Task AllEventsExportedWhenAllOversized()
	{
		const int totalEvents = 20;
		const long budget = 100;

		using var channel = new SizedEventChannel(new BufferOptions
		{
			OutboundBufferMaxSize = 100,
			OutboundBufferMaxBytes = budget,
		});

		for (var i = 0; i < totalEvents; i++)
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 999 });

		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(5));

		channel.TotalExported.Should().Be(totalEvents, "oversized events must not be dropped");
	}

	// ── concurrent writers ───────────────────────────────────────────────────
	// These tests are intentionally written to be non-flaky: they only assert on totals
	// and per-batch invariants (not on exact batch count or ordering, which are timing-dependent).

	[Test]
	public async Task ConcurrentWritersNoDrops()
	{
		// 4 threads each write 250 events concurrently. All 1000 must appear in export.
		const int writers = 4;
		const int eventsPerWriter = 250;
		const int total = writers * eventsPerWriter;

		using var channel = new SizedEventChannel(new BufferOptions
		{
			OutboundBufferMaxSize = 50,
			OutboundBufferMaxBytes = 5000,
		});

		await Task.WhenAll(Enumerable.Range(0, writers).Select(async _ =>
		{
			for (var i = 0; i < eventsPerWriter; i++)
				await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 100 });
		}));

		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(15));

		channel.TotalExported.Should().Be(total, "all events from all writers must be exported");
	}

	[Test]
	public async Task ConcurrentWritersPerBatchByteBudgetRespected()
	{
		// 6 concurrent writers, byte budget active. Per-batch invariant must hold regardless
		// of interleaving. (Total count is also asserted to catch any drops.)
		const int writers = 6;
		const int eventsPerWriter = 100;
		const int total = writers * eventsPerWriter;
		const long budget = 3000;

		using var channel = new SizedEventChannel(new BufferOptions
		{
			OutboundBufferMaxSize = 200,
			OutboundBufferMaxBytes = budget,
		});

		await Task.WhenAll(Enumerable.Range(0, writers).Select(async _ =>
		{
			for (var i = 0; i < eventsPerWriter; i++)
				await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 400 });
		}));

		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(15));

		channel.TotalExported.Should().Be(total, "no drops under concurrent load");
		AssertByteBudgetRespected(channel.ExportedBatches.ToArray(), budget);
	}

	[Test]
	public async Task ConcurrentWritersMixedSizesNoDrops()
	{
		// 5 concurrent writers producing varied event sizes. Only the total count is asserted
		// (batch structure is non-deterministic under concurrency).
		const int writers = 5;
		const int eventsPerWriter = 80;
		const int total = writers * eventsPerWriter;

		using var channel = new SizedEventChannel(new BufferOptions
		{
			OutboundBufferMaxSize = 30,
			OutboundBufferMaxBytes = 2000,
		});

		await Task.WhenAll(Enumerable.Range(0, writers).Select(async writerIndex =>
		{
			for (var i = 0; i < eventsPerWriter; i++)
			{
				var size = (writerIndex * 100 + i * 37) % 4 switch { 0 => 50L, 1 => 300L, 2 => 800L, _ => 1500L };
				await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = size });
			}
		}));

		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(15));

		channel.TotalExported.Should().Be(total, "no drops under concurrent mixed-size load");
	}

	[Test]
	public async Task HighVolumeConcurrentWritesNoDrops()
	{
		// High-volume stress: 8 writers × 500 events = 4000 total. Byte budget is active.
		const int writers = 8;
		const int eventsPerWriter = 500;
		const int total = writers * eventsPerWriter;

		using var channel = new SizedEventChannel(new BufferOptions
		{
			InboundBufferMaxSize = 10_000,
			OutboundBufferMaxSize = 100,
			OutboundBufferMaxBytes = 5000,
		});

		await Task.WhenAll(Enumerable.Range(0, writers).Select(async _ =>
		{
			for (var i = 0; i < eventsPerWriter; i++)
				await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 100 });
		}));

		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(20));

		channel.TotalExported.Should().Be(total, "no events should be dropped under high-volume concurrent writes");
	}

	[Test]
	public async Task MultipleChannelsAreIndependent()
	{
		// Two channels with different byte budgets running concurrently must not interfere with
		// each other's flush decisions.
		const int eventsEach = 50;

		using var narrow = new SizedEventChannel(new BufferOptions
		{
			OutboundBufferMaxSize = 100,
			OutboundBufferMaxBytes = 200,   // tiny budget → many small batches
		});
		using var wide = new SizedEventChannel(new BufferOptions
		{
			OutboundBufferMaxSize = 100,
			OutboundBufferMaxBytes = 100_000, // huge budget → few batches
		});

		await Task.WhenAll(
			Task.Run(async () =>
			{
				for (var i = 0; i < eventsEach; i++)
					await narrow.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 150 });
			}),
			Task.Run(async () =>
			{
				for (var i = 0; i < eventsEach; i++)
					await wide.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 150 });
			})
		);

		await Task.WhenAll(
			narrow.WaitForDrainAsync(TimeSpan.FromSeconds(10)).AsTask(),
			wide.WaitForDrainAsync(TimeSpan.FromSeconds(10)).AsTask()
		);

		narrow.TotalExported.Should().Be(eventsEach, "narrow channel must not drop events");
		wide.TotalExported.Should().Be(eventsEach, "wide channel must not drop events");

		// Each channel's batches must respect its own budget.
		AssertByteBudgetRespected(narrow.ExportedBatches.ToArray(), 200);
		AssertByteBudgetRespected(wide.ExportedBatches.ToArray(), 100_000);
	}

	// ── pathological sequences ───────────────────────────────────────────────

	[Test]
	public async Task AlternatingSmallAndOversizedEventsAreAllExported()
	{
		// Alternating pattern: small (100 bytes), oversized (2000 bytes).
		// Each oversized event forces a flush of any accumulated smalls, then gets its own batch.
		const int pairs = 15;
		const long budget = 500;

		using var channel = new SizedEventChannel(new BufferOptions
		{
			OutboundBufferMaxSize = 100,
			OutboundBufferMaxBytes = budget,
		});

		for (var i = 0; i < pairs; i++)
		{
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 100 });
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 2000 });
		}

		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(10));

		channel.TotalExported.Should().Be(pairs * 2, "no drops for alternating small/oversized");
		AssertByteBudgetRespected(channel.ExportedBatches.ToArray(), budget);
	}

	[Test]
	public async Task MonotonicallyIncreasingSizeEventsAreAllExported()
	{
		// Events grow from 1 byte to a size that exceeds the budget.
		// Tests that the average-based pre-flush adapts as item sizes grow.
		const long budget = 1000;

		using var channel = new SizedEventChannel(new BufferOptions
		{
			OutboundBufferMaxSize = 100,
			OutboundBufferMaxBytes = budget,
		});

		for (var i = 1; i <= 30; i++)
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = i * 50 });

		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(5));

		channel.TotalExported.Should().Be(30, "no drops for monotonically growing event sizes");
		AssertByteBudgetRespected(channel.ExportedBatches.ToArray(), budget);
	}

	[Test]
	public async Task BurstFollowedByQuietDrainsCompletely()
	{
		// Write a large burst (fills inbound) then stop. Everything should still drain.
		const int burst = 300;
		const long budget = 2000;

		using var channel = new SizedEventChannel(new BufferOptions
		{
			InboundBufferMaxSize = 1000,
			OutboundBufferMaxSize = 50,
			OutboundBufferMaxBytes = budget,
		});

		for (var i = 0; i < burst; i++)
			await channel.WaitToWriteAsync(new SizedEventChannel.Event { SizeInBytes = 300 });

		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(15));

		channel.TotalExported.Should().Be(burst, "all burst events must eventually be exported");
		AssertByteBudgetRespected(channel.ExportedBatches.ToArray(), budget);
	}
}
