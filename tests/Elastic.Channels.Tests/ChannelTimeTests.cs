// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#nullable enable

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Channels.Tests;

/// <summary>
/// The channel's time on a fake clock: a partial buffer is flushed when its lifetime has elapsed, not before and not after, a retry waits for its
/// backoff, a drain gives up at its limit. Time moves only when a spec moves it, so nothing here depends on how fast the machine is.
/// The only real time is the short polling tick of <see cref="UntilAsync"/> while the channel's own threads catch up.
/// </summary>
public class ChannelTimeTests
{
	private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);

	/// <summary>Waits, on the real clock, for the channel's threads to reach a state. A hang detector: it says nothing about timing.</summary>
	private static async Task UntilAsync(Func<bool> condition, string what)
	{
		var stop = Stopwatch.StartNew();
		while (!condition())
		{
			if (stop.Elapsed > TimeSpan.FromSeconds(60)) throw new TimeoutException($"Gave up waiting for: {what}");
			await Task.Delay(1).ConfigureAwait(false);
		}
	}

	private static ScriptedChannel Channel(RecordingTimeProvider time, int outbound = 50, Func<int, ArraySegment<Item>, CancellationToken, Task<Response>>? export = null,
		Func<int, TimeSpan>? backoff = null) =>
		new(ScriptedChannel.Opts(inbound: 100, outbound: outbound, concurrency: 1, lifetime: Lifetime, time: time,
			configure: o => { if (backoff != null) o.BufferOptions.ExportBackoffPeriod = backoff; }),
			export == null ? null : (call, buffer, ct) => export(call, buffer, ct));

	private static async Task WriteAndWaitForTheTimer(ScriptedChannel channel, RecordingTimeProvider time, int count, int from = 0)
	{
		var armsBefore = time.Arms;
		for (var i = from; i < from + count; i++)
			channel.TryWrite(new Item(i)).Should().BeTrue();
		// the loop has read the items and armed the timer for the rest of the buffer's lifetime
		await UntilAsync(() => channel.InboundBuffer.Count == count && time.Arms > armsBefore, "the items are in the buffer and the timer is armed").ConfigureAwait(false);
	}

	[Test]
	public async Task APartialBufferIsFlushedExactlyWhenItsLifetimeHasElapsed()
	{
		var time = new RecordingTimeProvider();
		using var channel = Channel(time);

		await WriteAndWaitForTheTimer(channel, time, 3).ConfigureAwait(false);

		time.Advance(Lifetime - TimeSpan.FromMilliseconds(1));
		await Task.Delay(100).ConfigureAwait(false);
		channel.Calls.Should().BeEmpty("one millisecond of the lifetime is left");

		time.Advance(TimeSpan.FromMilliseconds(1));
		await UntilAsync(() => channel.Calls.Count == 1, "the buffer is flushed once its lifetime has elapsed").ConfigureAwait(false);
		channel.Calls.Single().Ids.Should().Equal(0, 1, 2);
	}

	[Test]
	public async Task TheTimerIsArmedAgainForTheNextBuffer()
	{
		var time = new RecordingTimeProvider();
		using var channel = Channel(time);

		await WriteAndWaitForTheTimer(channel, time, 3).ConfigureAwait(false);
		time.Advance(Lifetime);
		await UntilAsync(() => channel.Calls.Count == 1, "the first buffer is flushed").ConfigureAwait(false);

		await WriteAndWaitForTheTimer(channel, time, 2, from: 3).ConfigureAwait(false);
		time.Advance(Lifetime - TimeSpan.FromMilliseconds(1));
		await Task.Delay(100).ConfigureAwait(false);
		channel.Calls.Should().ContainSingle("the lifetime of the second buffer counts from its own start");

		time.Advance(TimeSpan.FromMilliseconds(1));
		await UntilAsync(() => channel.Calls.Count == 2, "the second buffer is flushed").ConfigureAwait(false);
		channel.Calls.Select(c => c.Ids.Length).Should().Equal(3, 2);
	}

	[Test]
	public async Task ATimerThatFiresEarlyIsArmedAgainAndDoesNotFlushEarly()
	{
		var time = new RecordingTimeProvider();
		using var channel = Channel(time);
		await WriteAndWaitForTheTimer(channel, time, 3).ConfigureAwait(false);
		time.Advance(Lifetime - TimeSpan.FromSeconds(2));

		// the timer fires with three seconds of the lifetime left: the loop looks at the clock, finds the buffer is not due and waits again
		var armsBefore = time.Arms;
		time.FireAllTimersNow();
		await UntilAsync(() => time.Arms > armsBefore, "the loop arms the timer again for the rest of the lifetime").ConfigureAwait(false);
		await Task.Delay(100).ConfigureAwait(false);
		channel.Calls.Should().BeEmpty();

		time.Advance(TimeSpan.FromSeconds(2));
		await UntilAsync(() => channel.Calls.Count == 1, "the buffer is flushed when the lifetime is over").ConfigureAwait(false);
	}

	[Test]
	public async Task AFullBufferDoesNotWaitForTheTimer()
	{
		var time = new RecordingTimeProvider();
		using var channel = Channel(time, outbound: 10);

		for (var i = 0; i < 10; i++) channel.TryWrite(new Item(i)).Should().BeTrue();

		await UntilAsync(() => channel.Calls.Count == 1, "a full buffer is flushed without any time passing").ConfigureAwait(false);
	}

	[Test]
	public async Task ASetBackWallClockDoesNotChangeWhenABufferIsFlushed()
	{
		var time = new RecordingTimeProvider();
		using var channel = Channel(time);
		await WriteAndWaitForTheTimer(channel, time, 3).ConfigureAwait(false);

		// an NTP step: the wall clock goes back an hour, the monotonic clock does not
		time.WallClockOffset = -TimeSpan.FromHours(1);
		time.Advance(Lifetime);

		await UntilAsync(() => channel.Calls.Count == 1, "the buffer ages on the monotonic clock").ConfigureAwait(false);
	}

	[Test]
	public async Task ARetryWaitsForItsBackoffOnTheTimeProvider()
	{
		var time = new RecordingTimeProvider();
		using var channel = Channel(time, outbound: 3, backoff: _ => TimeSpan.FromSeconds(10),
			export: (call, _, _) => Task.FromResult(call == 1 ? new Response { RetryWhole = true } : new Response()));

		for (var i = 0; i < 3; i++) channel.TryWrite(new Item(i)).Should().BeTrue();
		await UntilAsync(() => channel.Calls.Count == 1, "the first attempt").ConfigureAwait(false);
		await UntilAsync(() => time.TimersCreated >= 2, "the backoff timer exists").ConfigureAwait(false);

		time.Advance(TimeSpan.FromSeconds(9));
		await Task.Delay(100).ConfigureAwait(false);
		channel.Calls.Should().ContainSingle("the backoff is 10 seconds");

		time.Advance(TimeSpan.FromSeconds(1));
		await UntilAsync(() => channel.Calls.Count == 2, "the retry").ConfigureAwait(false);
	}

	[Test]
	public async Task ADrainGivesUpAtItsLimitOnTheTimeProvider()
	{
		var time = new RecordingTimeProvider();
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var channel = Channel(time, outbound: 3, export: async (_, _, _) => { await release.Task.ConfigureAwait(false); return new Response(); });
		for (var i = 0; i < 3; i++) channel.TryWrite(new Item(i)).Should().BeTrue();
		await UntilAsync(() => channel.InflightExportOperations == 1, "the export is running").ConfigureAwait(false);

		var drain = channel.WaitForDrainAsync(TimeSpan.FromSeconds(30)).AsTask();
		await UntilAsync(() => time.TimersCreated >= 2, "the drain's limit is a timer").ConfigureAwait(false);

		time.Advance(TimeSpan.FromSeconds(29));
		await Task.Delay(100).ConfigureAwait(false);
		drain.IsCompleted.Should().BeFalse("29 of 30 seconds have passed");

		time.Advance(TimeSpan.FromSeconds(1));
		(await drain.ConfigureAwait(false)).Should().BeFalse();
		release.TrySetResult();
	}

	// the system clock still works: the same flush on real time, with a limit meant for a slow runner
	[Test]
	public async Task APartialBufferIsFlushedOnTheSystemClock()
	{
		using var channel = new ScriptedChannel(ScriptedChannel.Opts(inbound: 100, outbound: 50, concurrency: 1, lifetime: TimeSpan.FromMilliseconds(100)));

		for (var i = 0; i < 3; i++) channel.TryWrite(new Item(i)).Should().BeTrue();

		await UntilAsync(() => channel.Calls.Count == 1, "the buffer is flushed on the system clock").ConfigureAwait(false);
	}
}
