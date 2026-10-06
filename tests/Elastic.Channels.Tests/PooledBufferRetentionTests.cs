// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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
}
