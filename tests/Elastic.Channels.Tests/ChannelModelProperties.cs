// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Channels.Tests;

/// <summary>
/// The channel against a model of its documented behaviour: every item written reaches exactly one end, delivered by a
/// completed export or reported as exhausted, and the number of attempts follows from the retry budget.
/// The model is written from the behaviour the options describe (ExportMaxRetries, the retry predicate, the exhausted
/// callback), not from the code, and each property was mutation-checked.
/// </summary>
public class ChannelModelProperties
{
	/// <summary>
	/// Items asked to be retried a number of times each. The model: an item that is retried <c>p</c> times is delivered on its
	/// attempt <c>p + 1</c> when that is within the budget (<c>ExportMaxRetries + 1</c> attempts), and is reported as exhausted otherwise.
	/// </summary>
	[Test]
	public async Task EveryItemIsDeliveredOnceOrReportedExhaustedWhateverTheServerAsksToRetry()
	{
		var scenarios = Gen.Select(Gen.Int[0, 40], Gen.Int[5, 60], Gen.Int[1, 12], Gen.Int[1, 3], Gen.Int[0, 3], Gen.Int[0, 5].Array[40]);
		await scenarios.SampleAsync(async x =>
		{
			var (n, inbound, outbound, concurrency, maxRetries, plan) = x;
			var exhausted = new ConcurrentBag<int>();
			var attempts = new ConcurrentDictionary<int, int>();
			var options = ScriptedChannel.Opts(inbound, outbound, concurrency, maxRetries, lifetime: TimeSpan.FromHours(1),
				configure: o => o.ExportMaxRetriesCallback = items => { foreach (var i in items) exhausted.Add(i.Id); });
			using var channel = new ScriptedChannel(options, (_, buffer, _) =>
			{
				var retry = new HashSet<int>();
				foreach (var item in buffer)
				{
					var attempt = attempts.AddOrUpdate(item.Id, 0, (_, seen) => seen + 1);
					if (attempt < plan[item.Id]) retry.Add(item.Id);
				}
				return Task.FromResult(new Response { Retry = retry });
			});

			for (var i = 0; i < n; i++)
				(await channel.WaitToWriteAsync(new Item(i)).ConfigureAwait(false)).Should().BeTrue();
			var drained = await Task.Run(() => channel.WaitForDrainAsync(TimeSpan.FromSeconds(20)).AsTask()).ConfigureAwait(false);

			drained.Should().BeTrue();
			var exportCalls = channel.Calls.SelectMany(c => c.Ids).GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count());
			for (var id = 0; id < n; id++)
			{
				var retried = plan[id];
				var expectedAttempts = Math.Min(retried, maxRetries) + 1;
				exportCalls[id].Should().Be(expectedAttempts, $"item {id} asks for {retried} retries with a budget of {maxRetries}");
				exhausted.Count(e => e == id).Should().Be(retried > maxRetries ? 1 : 0, $"item {id} is exhausted exactly when it needs more retries than the budget");
			}
			channel.InflightEvents.Should().Be(0);
			channel.InflightExportOperations.Should().Be(0);
		}, iter: 40);
	}

	public enum Outcome { Ok, RetryWhole, Throw }

	/// <summary>
	/// Whole batches that succeed, ask to be retried, or throw (and the caller says whether a throw is retried). With one worker and no time based flush the batches are
	/// chunks of <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.BatchExportSize"/> in write order, so the
	/// model can replay them: each call takes the next outcome of the script, an exception counts as an attempt like a retry does
	/// when the caller allows it and ends the batch when not, and a batch that is still pending after the last attempt is reported as exhausted.
	/// </summary>
	[Test]
	public async Task ABatchEndsOrRetriesAfterAThrowAsTheCallerDecided()
	{
		var scenarios = Gen.Select(Gen.Int[1, 40], Gen.Int[5, 60], Gen.Int[1, 12], Gen.Int[0, 3], Gen.Enum<Outcome>().Array[60], Gen.Bool);
		await scenarios.SampleAsync(async x =>
		{
			var (n, inbound, outbound, maxRetries, script, retryOnException) = x;
			var exhausted = new ConcurrentBag<int>();
			var options = ScriptedChannel.Opts(inbound, outbound, concurrency: 1, maxRetries, lifetime: TimeSpan.FromHours(1),
				configure: o =>
				{
					o.ExportMaxRetriesCallback = items => { foreach (var i in items) exhausted.Add(i.Id); };
					o.BufferOptions.ExportRetryOnException = _ => retryOnException;
				});
			using var channel = new ScriptedChannel(options, (call, _, _) => script[(call - 1) % script.Length] switch
			{
				Outcome.Throw => throw new InvalidOperationException("scripted"),
				Outcome.RetryWhole => Task.FromResult(new Response { RetryWhole = true }),
				_ => Task.FromResult(new Response())
			});

			for (var i = 0; i < n; i++)
				(await channel.WaitToWriteAsync(new Item(i)).ConfigureAwait(false)).Should().BeTrue();
			var drained = await Task.Run(() => channel.WaitForDrainAsync(TimeSpan.FromSeconds(20)).AsTask()).ConfigureAwait(false);
			drained.Should().BeTrue();

			// replay
			var size = channel.BatchExportSize;
			var call = 0;
			var expectedDelivered = new List<int>();
			var expectedExhausted = new List<int>();
			for (var start = 0; start < n; start += size)
			{
				var ids = Enumerable.Range(start, Math.Min(size, n - start)).ToArray();
				var done = false;
				for (var attempt = 0; attempt <= maxRetries && !done; attempt++)
				{
					var outcome = script[call++ % script.Length];
					if (outcome == Outcome.Ok) { expectedDelivered.AddRange(ids); done = true; }
					// a throw ends the batch unless the caller said it is safe to try again
					else if (outcome == Outcome.Throw && !retryOnException) break;
				}
				if (!done) expectedExhausted.AddRange(ids);
			}

			channel.Calls.Count.Should().Be(call, "one call per attempt");
			exhausted.OrderBy(i => i).Should().Equal(expectedExhausted.OrderBy(i => i));
		}, iter: 40);
	}

	/// <summary>
	/// A full inbound channel that drops items (the oldest, the newest, or the one being written): every item the channel accepted
	/// is delivered or reported as dropped, never both and never neither, and the drain still completes.
	/// </summary>
	[Test]
	public async Task EveryAcceptedItemIsDeliveredOrDroppedWhenTheInboundChannelOverflows()
	{
		var modes = new[] { System.Threading.Channels.BoundedChannelFullMode.DropOldest, System.Threading.Channels.BoundedChannelFullMode.DropNewest, System.Threading.Channels.BoundedChannelFullMode.DropWrite };
		var scenarios = Gen.Select(Gen.Int[1, 200], Gen.Int[2, 30], Gen.Int[1, 10], Gen.Int[0, 2].Select(i => modes[i]));
		await scenarios.SampleAsync(async x =>
		{
			var (n, inbound, outbound, mode) = x;
			var dropped = new ConcurrentBag<int>();
			var accepted = new ConcurrentBag<int>();
			var options = ScriptedChannel.Opts(inbound, outbound, concurrency: 1, lifetime: TimeSpan.FromMilliseconds(20), configure: o =>
			{
				o.BufferOptions.BoundedChannelFullMode = mode;
				o.BufferItemDropped = item => dropped.Add(item.Id);
			});
			using var channel = new ScriptedChannel(options, async (_, _, ct) => { await Task.Delay(1, ct).ConfigureAwait(false); return new Response(); });

			for (var i = 0; i < n; i++)
				if (channel.TryWrite(new Item(i)))
					accepted.Add(i);
			var drained = await Task.Run(() => channel.WaitForDrainAsync(TimeSpan.FromSeconds(20)).AsTask()).ConfigureAwait(false);

			drained.Should().BeTrue("the ledger must count dropped items as ended, or the drain never completes");
			var delivered = channel.SeenByCompletedExports();
			delivered.Distinct().Count().Should().Be(delivered.Length, "nothing is delivered twice");
			dropped.Distinct().Count().Should().Be(dropped.Count, "nothing is dropped twice");
			delivered.Intersect(dropped).Should().BeEmpty("an item is delivered or dropped, not both");
			delivered.Union(dropped).OrderBy(i => i).Should().BeEquivalentTo(accepted.OrderBy(i => i), o => o.WithStrictOrdering(), "every accepted item reached an end");
			channel.InflightEvents.Should().Be(0);
		}, iter: 40);
	}
}
