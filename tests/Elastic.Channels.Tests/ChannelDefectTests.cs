// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Channels.Tests;

/// <summary>
/// One spec per defect found by reading BufferedChannelBase. A spec that fails on the current code is skipped with a BUG tag
/// and enabled by the change that fixes it.
/// </summary>
public class ChannelDefectTests
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

	// WaitForDrainAsync blocks its caller before the first await, so the call itself runs on a pool thread: a hang then fails
	// the test after a few seconds instead of hanging it. The hung thread stays behind, which is what a bug does.
	private static async Task<T> Within<T>(Func<Task<T>> call, string because)
	{
		try
		{
			return await Task.Run(call).WaitAsync(Wait).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			throw new InvalidOperationException($"Timed out after {Wait}: {because}");
		}
	}

	private static Task<bool> Drain(ScriptedChannel channel, TimeSpan? maxWait = null) =>
		Within(() => channel.WaitForDrainAsync(maxWait ?? Wait).AsTask(), "drain must return");

	private static void Write(ScriptedChannel channel, int count, int from = 0)
	{
		for (var i = from; i < from + count; i++)
			channel.TryWrite(new Item(i)).Should().BeTrue();
	}

	// 1. an export that throws is retried
	[Test, Skip("BUG 1: an ExportAsync exception before the last attempt leaves the response null, so the batch is dropped and never retried")]
	public async Task AnExportThatThrowsOnAnEarlyAttemptIsRetried()
	{
		var exhausted = new List<int>();
		var options = ScriptedChannel.Opts(configure: o => o.ExportMaxRetriesCallback = items => exhausted.AddRange(items.Select(i => i.Id)));
		using var channel = new ScriptedChannel(options, (call, _, _) =>
			call == 1 ? throw new InvalidOperationException("connection reset") : Task.FromResult(new Response()));

		Write(channel, 5);
		var drained = await Drain(channel).ConfigureAwait(false);

		drained.Should().BeTrue();
		channel.SeenByCompletedExports().OrderBy(i => i).Should().BeEquivalentTo([0, 1, 2, 3, 4], o => o.WithStrictOrdering(), "the second attempt exports the batch again");
		exhausted.Should().BeEmpty();
	}

	// 1b. when every attempt throws, the items are reported as exhausted
	[Test, Skip("BUG 1: items whose export threw are never reported to ExportMaxRetriesCallback")]
	public async Task AnExportThatAlwaysThrowsReportsItsItemsAsExhausted()
	{
		var exhausted = new List<int>();
		var options = ScriptedChannel.Opts(maxRetries: 2, configure: o => o.ExportMaxRetriesCallback = items => exhausted.AddRange(items.Select(i => i.Id)));
		using var channel = new ScriptedChannel(options, (_, _, _) => throw new InvalidOperationException("down"));

		Write(channel, 3);
		await Drain(channel).ConfigureAwait(false);

		exhausted.OrderBy(i => i).Should().Equal(0, 1, 2);
		channel.Calls.Count.Should().Be(3, "the first try and two retries");
	}

	// 2. drain returns true only when everything was handled
	[Test]
	public async Task DrainReturningTrueMeansEveryItemWasExported()
	{
		using var channel = new ScriptedChannel(
			ScriptedChannel.Opts(inbound: 1000, outbound: 100, concurrency: 2, lifetime: TimeSpan.FromSeconds(30)),
			async (_, _, ct) => { await Task.Delay(30, ct).ConfigureAwait(false); return new Response(); });

		Write(channel, 450);
		var drained = await Drain(channel, TimeSpan.FromSeconds(20)).ConfigureAwait(false);

		drained.Should().BeTrue();
		channel.SeenByCompletedExports().Distinct().Count().Should().Be(450);
	}

	// 4. two exports ending together with a user handle that has one count left
	[Test]
	public async Task TwoExportsEndingTogetherDoNotBreakTheChannel()
	{
		for (var round = 0; round < 50; round++)
		{
			var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var inside = 0;
			var options = ScriptedChannel.Opts(inbound: 40, outbound: 10, concurrency: 2, configure: o =>
			{
				o.BufferOptions.WaitHandle = new CountdownEvent(1);
				o.OutboundChannelExitedCallback = () => exited.TrySetResult();
			});
			using var channel = new ScriptedChannel(options, async (_, _, ct) =>
			{
				if (Interlocked.Increment(ref inside) == 2) gate.TrySetResult();
				try { await gate.Task.WaitAsync(TimeSpan.FromMilliseconds(200), ct).ConfigureAwait(false); }
				catch (TimeoutException) { }
				return new Response();
			});

			Write(channel, 20);
			_ = channel.TryComplete();
			await Within(() => exited.Task.ContinueWith(_ => true, TaskScheduler.Default), $"the outbound loop must exit cleanly (round {round})").ConfigureAwait(false);
		}
	}

	// 5. completing the channel with an error ends it
	[Test, Skip("BUG 5: TryComplete(error) faults the inbound reader, the read loop treats the fault as Read and spins forever")]
	public async Task CompletingWithAnErrorEndsTheChannel()
	{
		using var channel = new ScriptedChannel(ScriptedChannel.Opts());
		Write(channel, 3);

		_ = channel.TryComplete(new InvalidOperationException("producer failed"));

		var drained = await Drain(channel, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
		drained.Should().BeTrue("items written before the error were exported");
	}

	// 6. a throwing retry callback does not leave the export counted as running
	[Test, Skip("BUG 6: the retry callback runs outside any finally, so a throw skips the in-flight export decrement and the count stays at 1")]
	public async Task AThrowingRetryCallbackDoesNotLeaveAnExportRunning()
	{
		var options = ScriptedChannel.Opts(configure: o => o.ExportRetryCallback = _ => throw new InvalidOperationException("listener bug"));
		using var channel = new ScriptedChannel(options, (call, _, _) =>
			Task.FromResult(call == 1 ? new Response { RetryWhole = true } : new Response()));

		Write(channel, 2);
		_ = await Drain(channel, TimeSpan.FromSeconds(3)).ConfigureAwait(false);

		channel.InflightExportOperations.Should().Be(0);
	}

	// 9. Dispose can be called twice, and after the channel finished
	[Test]
	public async Task DisposeIsIdempotent()
	{
		var channel = new ScriptedChannel(ScriptedChannel.Opts());
		Write(channel, 5);
		_ = await Drain(channel).ConfigureAwait(false);

		channel.Dispose();
		Action again = channel.Dispose;

		again.Should().NotThrow();
	}

	// 13. an inbound buffer smaller than the export concurrency still exports
	[Test, Skip("BUG 13: InboundBufferMaxSize below the concurrency makes BatchExportSize 0 and WaitForDrainAsync divides by zero")]
	public async Task AnInboundBufferSmallerThanTheConcurrencyStillExports()
	{
		using var channel = new ScriptedChannel(ScriptedChannel.Opts(inbound: 1, outbound: 10, concurrency: 4));

		Write(channel, 1);
		var drained = await Drain(channel, TimeSpan.FromSeconds(3)).ConfigureAwait(false);

		drained.Should().BeTrue();
		channel.SeenByCompletedExports().Should().Equal(0);
	}

	// 10. the count of events in flight never goes negative, and ends at zero
	[Test, Skip("BUG 10: every tenth read timeout resets the in-flight counter to 0 while writers still increment it after TryWrite")]
	public async Task InflightEventsNeverGoNegative()
	{
		using var channel = new ScriptedChannel(ScriptedChannel.Opts(inbound: 200, outbound: 10, concurrency: 2, lifetime: TimeSpan.FromMilliseconds(5)));
		var lowest = long.MaxValue;
		using var sampling = new CancellationTokenSource();
		var sampler = Task.Run(() =>
		{
			while (!sampling.IsCancellationRequested)
				lowest = Math.Min(lowest, channel.InflightEvents);
		});

		// a slow trickle, so the read loop times out between writes more than ten times
		for (var i = 0; i < 40; i++)
		{
			channel.TryWrite(new Item(i)).Should().BeTrue();
			await Task.Delay(12).ConfigureAwait(false);
		}
		_ = await Drain(channel).ConfigureAwait(false);
		sampling.Cancel();
		await sampler.ConfigureAwait(false);

		lowest.Should().BeGreaterThanOrEqualTo(0);
		channel.InflightEvents.Should().Be(0);
		channel.SeenByCompletedExports().Distinct().Count().Should().Be(40);
	}
}
