// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CsCheck;
using Elastic.Channels.Buffers;
using Elastic.Channels.Diagnostics;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Channels.Tests;

/// <summary>
/// Exported events must not stay reachable through the shared array pool (https://github.com/elastic/elastic-ingest-dotnet/issues/207).
/// A pooled array that keeps a reference to every event of its last batch retains their payloads until the pool happens to reuse or trim it.
/// </summary>
public class PooledBufferRetentionTests
{
	private sealed class Payload(int size)
	{
		public byte[] Data { get; } = new byte[size];
	}

	[Test]
	public void DisposingAnOutboundBufferClearsTheArrayItReturnsToThePool()
	{
		var inbound = new InboundBuffer<Payload>(maxBufferSize: 8, TimeSpan.FromSeconds(5));
		for (var i = 0; i < 5; i++) inbound.Add(new Payload(16));

		var outbound = new OutboundBuffer<Payload>(inbound);
		var segment = outbound.GetArraySegment();
		var array = segment.Array!;
		segment.Count.Should().Be(5);
		array.Take(5).Should().OnlyContain(p => p != null, "the batch is populated before it is disposed");

		outbound.Dispose();

		array.Should().OnlyContain(p => p == null, "the array goes back to a shared pool and must not keep the exported events alive");
	}

	[Test]
	public void ClearingOnlyCoversTheBatchAndNeverTouchesTheRestOfTheArray()
	{
		// a pooled array is usually larger than the batch, the clear is bounded by the batch size
		var inbound = new InboundBuffer<Payload>(maxBufferSize: 100, TimeSpan.FromSeconds(5));
		inbound.Add(new Payload(1));
		inbound.Add(new Payload(1));

		var outbound = new OutboundBuffer<Payload>(inbound);
		var array = outbound.GetArraySegment().Array!;
		array.Length.Should().BeGreaterThanOrEqualTo(100);

		outbound.Dispose();

		array.Take(2).Should().OnlyContain(p => p == null);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static async Task<List<WeakReference>> ExportAndTrackAsync(NoopBufferedChannel channel, int events)
	{
		var tracked = new List<WeakReference>();
		for (var i = 0; i < events; i++)
		{
			var e = new NoopBufferedChannel.NoopEvent { Id = i };
			tracked.Add(new WeakReference(e));
			(await channel.WaitToWriteAsync(e)).Should().BeTrue();
		}
		return tracked;
	}

	[Test]
	public async Task ExportedEventsBecomeCollectableOnceTheExportCompleted()
	{
		using var channel = new NoopBufferedChannel(new BufferOptions { OutboundBufferMaxSize = 4, ExportMaxConcurrency = 1, InboundBufferMaxSize = 64 });
		var tracked = await ExportAndTrackAsync(channel, 12);
		(await channel.WaitForDrainAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();

		for (var i = 0; i < 5; i++)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}

		tracked.Count(w => w.IsAlive).Should().Be(0, "no event may stay rooted through the pooled outbound array");
	}

	// ---- property tests: the contract must hold for every batch shape and every sequence of rents and returns ----

	[Test]
	public async Task EveryDisposedBatchLeavesAnEmptyArrayWhateverItsShape()
	{
		await Gen.Select(Gen.Int[1, 2_000], Gen.Double[0, 1]).SampleAsync(x =>
		{
			var (maxSize, fill) = x;
			var count = (int)(maxSize * fill);
			var inbound = new InboundBuffer<Payload>(maxSize, TimeSpan.FromSeconds(5));
			for (var i = 0; i < count; i++) inbound.Add(new Payload(1));

			var outbound = new OutboundBuffer<Payload>(inbound);
			var segment = outbound.GetArraySegment();
			var array = segment.Array!;
			segment.Count.Should().Be(count);
			array.Take(count).All(p => p != null).Should().BeTrue();

			outbound.Dispose();

			array.Should().OnlyContain(p => p == null);
			return Task.CompletedTask;
		}, iter: 300);
	}

	[Test]
	public async Task TheClearIsBoundedByTheBatchSoNothingBeyondItIsTouched()
	{
		await Gen.Select(Gen.Int[2, 500], Gen.Double[0, 1]).SampleAsync(x =>
		{
			var (maxSize, fill) = x;
			var count = Math.Min(maxSize - 1, (int)(maxSize * fill));
			var inbound = new InboundBuffer<Payload>(maxSize, TimeSpan.FromSeconds(5));
			for (var i = 0; i < count; i++) inbound.Add(new Payload(1));

			var outbound = new OutboundBuffer<Payload>(inbound);
			var array = outbound.GetArraySegment().Array!;
			// something the buffer does not own, sitting just past the batch
			var sentinel = new Payload(1);
			array[count] = sentinel;

			outbound.Dispose();

			array.Take(count).All(p => p == null).Should().BeTrue();
			array[count].Should().BeSameAs(sentinel, "only the populated range is cleared");
			array[count] = null!; // leave the shared pool clean for the next test
			return Task.CompletedTask;
		}, iter: 200);
	}

	[Test]
	public async Task NoBatchEverStartsWithReferencesLeftByAnEarlierBatchOfTheSamePool()
	{
		// the shared pool reuses arrays across batches (same thread, same size class), so a dirty return
		// shows up as stale events in the unused tail of the next batch's array
		var batch = Gen.Select(Gen.Int[1, 700], Gen.Double[0, 1]);
		await batch.List[2, 25].SampleAsync(batches =>
		{
			foreach (var (maxSize, fill) in batches)
			{
				var count = (int)(maxSize * fill);
				var inbound = new InboundBuffer<Payload>(maxSize, TimeSpan.FromSeconds(5));
				for (var i = 0; i < count; i++) inbound.Add(new Payload(1));

				var outbound = new OutboundBuffer<Payload>(inbound);
				var array = outbound.GetArraySegment().Array!;
				array.Skip(count).All(p => p == null).Should().BeTrue("a freshly rented array must not carry events of a previous batch");
				array.Take(count).All(p => p != null).Should().BeTrue();
				outbound.Dispose();
			}
			return Task.CompletedTask;
		}, iter: 300);
	}

	private static IEnumerable<NoopBufferedChannel.NoopEvent> Track(int count, List<WeakReference> tracked, int failAt)
	{
		for (var i = 0; i < count; i++)
		{
			if (i == failAt) throw new InvalidOperationException("source failed");
			var e = new NoopBufferedChannel.NoopEvent { Id = i };
			tracked.Add(new WeakReference(e));
			yield return e;
		}
	}

	private static int AliveAfterCollection(List<WeakReference> tracked)
	{
		for (var i = 0; i < 3; i++)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
		}
		return tracked.Count(w => w.IsAlive);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static async Task<List<WeakReference>> PushAsync(int events, int batch, int concurrency)
	{
		var tracked = new List<WeakReference>();
		using var channel = new NoopBufferedChannel(new BufferOptions { OutboundBufferMaxSize = batch, ExportMaxConcurrency = concurrency, InboundBufferMaxSize = 256 });
		foreach (var e in Track(events, tracked, failAt: -1))
			(await channel.WaitToWriteAsync(e)).Should().BeTrue();
		(await channel.WaitForDrainAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
		return tracked;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static async Task<List<WeakReference>> PullAsync(int events, int batch, int concurrency, int pullConcurrency, int failAt)
	{
		var tracked = new List<WeakReference>();
		using var channel = new NoopBufferedChannel(new BufferOptions { OutboundBufferMaxSize = batch, ExportMaxConcurrency = concurrency, InboundBufferMaxSize = 256 });
		try { await channel.IngestAllAsync(Track(events, tracked, failAt), pullConcurrency); }
		catch (InvalidOperationException) { /* an expected source failure */ }
		return tracked;
	}

	[Test]
	public async Task NoExportedEventStaysAliveForAnyShapeOfPushedTraffic()
	{
		await Gen.Select(Gen.Int[0, 80], Gen.Int[1, 16], Gen.Int[1, 3]).SampleAsync(async x =>
		{
			var tracked = await PushAsync(x.Item1, x.Item2, x.Item3);
			AliveAfterCollection(tracked).Should().Be(0);
		}, iter: 40);
	}

	[Test]
	public async Task NoPulledEventStaysAliveEvenWhenTheSourceFailsHalfway()
	{
		await Gen.Select(Gen.Int[0, 80], Gen.Int[1, 16], Gen.Int[1, 3], Gen.Int[1, 3], Gen.Int[-1, 80]).SampleAsync(async x =>
		{
			var tracked = await PullAsync(x.Item1, x.Item2, x.Item3, x.Item4, x.Item5);
			AliveAfterCollection(tracked).Should().Be(0);
		}, iter: 40);
	}
}
