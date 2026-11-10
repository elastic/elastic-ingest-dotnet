// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Diagnostics;
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

	// 1. an export that throws is not retried by default, and its items are reported instead of dropped silently
	[Test]
	public async Task AnExportThatThrowsGivesUpOnItsItemsAndReportsThem()
	{
		var exhausted = new List<int>();
		var thrown = new List<Exception>();
		var options = ScriptedChannel.Opts(configure: o =>
		{
			o.ExportMaxRetriesCallback = items => exhausted.AddRange(items.Select(i => i.Id));
			o.ExportExceptionCallback = thrown.Add;
		});
		using var channel = new ScriptedChannel(options, (call, _, _) =>
			call == 1 ? throw new InvalidOperationException("connection reset") : Task.FromResult(new Response()));

		Write(channel, 5);
		var drained = await Drain(channel).ConfigureAwait(false);

		drained.Should().BeTrue();
		exhausted.OrderBy(i => i).Should().BeEquivalentTo([0, 1, 2, 3, 4], o => o.WithStrictOrdering(), "the caller learns which items were given up on");
		thrown.Should().ContainSingle().Which.Message.Should().Be("connection reset");
		channel.Calls.Should().ContainSingle("without a response to pick from, and with unknown idempotence, nothing is sent again");
	}

	// 1b. the caller can say which exceptions are safe to retry
	[Test]
	public async Task AnExportThatThrowsIsRetriedWhenTheCallerSaysItIsSafe()
	{
		var exhausted = new List<int>();
		var options = ScriptedChannel.Opts(configure: o =>
		{
			o.ExportMaxRetriesCallback = items => exhausted.AddRange(items.Select(i => i.Id));
			o.BufferOptions.ExportRetryOnException = e => e is TimeoutException;
		});
		using var channel = new ScriptedChannel(options, (call, _, _) =>
			call == 1 ? throw new TimeoutException("timed out") : Task.FromResult(new Response()));

		Write(channel, 5);
		var drained = await Drain(channel).ConfigureAwait(false);

		drained.Should().BeTrue();
		channel.SeenByCompletedExports().OrderBy(i => i).Should().BeEquivalentTo([0, 1, 2, 3, 4], o => o.WithStrictOrdering(), "the second attempt exports the batch again");
		exhausted.Should().BeEmpty();
	}

	// 1c. an exception the predicate does not accept ends the export, and one it accepts is retried up to the budget
	[Test]
	public async Task OnlyTheExceptionsTheCallerAcceptsAreRetriedAndTheBudgetStillApplies()
	{
		var exhausted = new List<int>();
		var options = ScriptedChannel.Opts(maxRetries: 2, configure: o =>
		{
			o.ExportMaxRetriesCallback = items => exhausted.AddRange(items.Select(i => i.Id));
			o.BufferOptions.ExportRetryOnException = e => e is TimeoutException;
		});
		using var channel = new ScriptedChannel(options, (_, _, _) => throw new TimeoutException("down"));

		Write(channel, 3);
		_ = await Drain(channel).ConfigureAwait(false);

		exhausted.OrderBy(i => i).Should().BeEquivalentTo([0, 1, 2], o => o.WithStrictOrdering());
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

	// 2b. batches waiting behind a running export are not "drained"
	[Test]
	public async Task DrainDoesNotReturnTrueWhileBatchesAreStillQueued()
	{
		for (var round = 0; round < 10; round++)
		{
			// one item per batch and one worker: between two exports nothing is "in flight" for an instant
			using var channel = new ScriptedChannel(
				ScriptedChannel.Opts(inbound: 20, outbound: 1, concurrency: 1, lifetime: TimeSpan.FromHours(1)),
				async (_, _, ct) => { await Task.Delay(10, ct).ConfigureAwait(false); return new Response(); });

			Write(channel, 8);
			var drained = await Drain(channel, TimeSpan.FromSeconds(10)).ConfigureAwait(false);

			drained.Should().BeTrue();
			channel.Calls.Count.Should().Be(8, $"all eight batches were exported by the time drain returned (round {round})");
		}
	}

	// 14. WaitForDrainAsync is asynchronous: it hands back its task at once instead of blocking the caller until the wait is over
	[Test]
	public async Task WaitForDrainAsyncDoesNotBlockTheCaller()
	{
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var channel = new ScriptedChannel(
			ScriptedChannel.Opts(inbound: 100, outbound: 10, concurrency: 1),
			async (_, _, _) => { await release.Task.ConfigureAwait(false); return new Response(); });
		for (var i = 0; i < 60; i++)
			(await channel.WaitToWriteAsync(new Item(i)).ConfigureAwait(false)).Should().BeTrue();

		var stopwatch = Stopwatch.StartNew();
		var pending = channel.WaitForDrainAsync(TimeSpan.FromSeconds(3));
		var handedBack = stopwatch.Elapsed;
		var completedAtOnce = pending.IsCompleted;
		release.TrySetResult();
		var drained = await pending.ConfigureAwait(false);

		handedBack.Should().BeLessThan(TimeSpan.FromMilliseconds(500), "the call must not block its thread for the length of the wait");
		completedAtOnce.Should().BeFalse();
		drained.Should().BeTrue();
	}

	// 15. the drain wait ends at the limit the caller gave
	[Test]
	public async Task WaitForDrainAsyncReturnsFalseAtTheLimitWhenAnExportIsStuck()
	{
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var channel = new ScriptedChannel(
			ScriptedChannel.Opts(),
			async (_, _, _) => { await release.Task.ConfigureAwait(false); return new Response(); });
		Write(channel, 5);

		var stopwatch = Stopwatch.StartNew();
		var drained = await Drain(channel, TimeSpan.FromMilliseconds(400)).ConfigureAwait(false);
		var elapsed = stopwatch.Elapsed;
		release.TrySetResult();

		drained.Should().BeFalse();
		elapsed.Should().BeGreaterThan(TimeSpan.FromMilliseconds(350)).And.BeLessThan(TimeSpan.FromSeconds(3));
	}

	// 16. a drain that was cancelled by the caller says so
	[Test]
	public async Task WaitForDrainAsyncHonoursItsCancellationToken()
	{
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var channel = new ScriptedChannel(
			ScriptedChannel.Opts(),
			async (_, _, _) => { await release.Task.ConfigureAwait(false); return new Response(); });
		Write(channel, 5);
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

		var wait = async () => await channel.WaitForDrainAsync(TimeSpan.FromSeconds(30), cts.Token).ConfigureAwait(false);

		await wait.Should().ThrowAsync<OperationCanceledException>();
		release.TrySetResult();
	}

	// 17. a waiter for the drain is released, with false, when the channel is disposed under it
	[Test]
	public async Task DisposingTheChannelWhileWaitingForTheDrainAnswersFalse()
	{
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var channel = new ScriptedChannel(
			ScriptedChannel.Opts(),
			async (_, _, _) => { await release.Task.ConfigureAwait(false); return new Response(); });
		Write(channel, 5);
		var waiting = channel.WaitForDrainAsync(TimeSpan.FromSeconds(30)).AsTask();
		await Task.Delay(100).ConfigureAwait(false);

		await Task.Run(channel.Dispose).ConfigureAwait(false);
		var drained = await Within(() => waiting, "the waiter must be released by the dispose").ConfigureAwait(false);
		release.TrySetResult();

		drained.Should().BeFalse("the items were not exported");
	}

	private static async Task<bool> UntilAsync(Func<bool> condition, TimeSpan limit)
	{
		var stop = Stopwatch.StartNew();
		while (stop.Elapsed < limit)
		{
			if (condition()) return true;
			await Task.Delay(10).ConfigureAwait(false);
		}
		return condition();
	}

	// 18. a buffer that is not full is flushed when its lifetime ends, again and again
	[Test]
	public async Task APartialBufferIsFlushedWhenItsLifetimeEndsAndTheTimerIsArmedAgain()
	{
		using var channel = new ScriptedChannel(ScriptedChannel.Opts(inbound: 100, outbound: 50, concurrency: 1, lifetime: TimeSpan.FromMilliseconds(150)));

		Write(channel, 3);
		(await UntilAsync(() => channel.Calls.Count == 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false)).Should().BeTrue("the first partial buffer flushes on its own");

		Write(channel, 2, from: 3);
		(await UntilAsync(() => channel.Calls.Count == 2, TimeSpan.FromSeconds(5)).ConfigureAwait(false)).Should().BeTrue("so does the next one");

		channel.Calls.Select(c => c.Ids.Length).Should().BeEquivalentTo([3, 2], o => o.WithStrictOrdering());
	}

	// 19. ... and not before
	[Test]
	public async Task APartialBufferIsNotFlushedBeforeItsLifetimeEnds()
	{
		using var channel = new ScriptedChannel(ScriptedChannel.Opts(inbound: 100, outbound: 50, concurrency: 1, lifetime: TimeSpan.FromMilliseconds(600)));

		Write(channel, 3);
		await Task.Delay(150).ConfigureAwait(false);
		channel.Calls.Should().BeEmpty();

		(await UntilAsync(() => channel.Calls.Count == 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false)).Should().BeTrue();
	}

	// 20. a full buffer goes at once, and does not wait for the timer
	[Test]
	public async Task AFullBufferIsFlushedWithoutWaitingForItsLifetime()
	{
		using var channel = new ScriptedChannel(ScriptedChannel.Opts(inbound: 100, outbound: 10, concurrency: 1, lifetime: TimeSpan.FromHours(1)));

		Write(channel, 10);

		(await UntilAsync(() => channel.Calls.Count == 1, TimeSpan.FromSeconds(5)).ConfigureAwait(false)).Should().BeTrue();
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
	[Test]
	public async Task CompletingWithAnErrorEndsTheChannel()
	{
		using var channel = new ScriptedChannel(ScriptedChannel.Opts());
		Write(channel, 3);

		_ = channel.TryComplete(new InvalidOperationException("producer failed"));

		var drained = await Drain(channel, TimeSpan.FromSeconds(3)).ConfigureAwait(false);
		drained.Should().BeTrue("items written before the error were exported");
	}

	// 6. a throwing retry callback does not leave the export counted as running
	[Test]
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
	[Test]
	public async Task AnInboundBufferSmallerThanTheConcurrencyStillExports()
	{
		using var channel = new ScriptedChannel(ScriptedChannel.Opts(inbound: 1, outbound: 10, concurrency: 4));

		Write(channel, 1);
		var drained = await Drain(channel, TimeSpan.FromSeconds(3)).ConfigureAwait(false);

		drained.Should().BeTrue();
		channel.SeenByCompletedExports().Should().Equal(0);
	}

	// 10. the count of events in flight never goes negative, and ends at zero
	[Test]
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
