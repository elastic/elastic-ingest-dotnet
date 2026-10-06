// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.Indices;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests.Bulk;

/// <summary>Pull mode on a channel: a plain <see cref="IndexChannelOptions{TEvent}"/> without any mapping context.</summary>
public class IngestAllChannelTests
{
	private static TestDocument[] Documents(int n) =>
		Enumerable.Range(0, n).Select(i => new TestDocument { Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(i) }).ToArray();

	private static IndexChannel<TestDocument> Channel(ScriptedTransport t, int batch, int concurrency = 1, int retries = 1, TimeSpan? lifetime = null) =>
		new(new IndexChannelOptions<TestDocument>(t.Transport)
		{
			IndexFormat = "my-index",
			BufferOptions = new BufferOptions
			{
				OutboundBufferMaxSize = batch,
				ExportMaxConcurrency = concurrency,
				ExportMaxRetries = retries,
				ExportBackoffPeriod = _ => TimeSpan.FromMilliseconds(1),
				OutboundBufferMaxLifetime = lifetime ?? TimeSpan.FromSeconds(5)
			}
		});

	private static long[] Seconds(CapturedRequest r) =>
		r.Lines.Where((_, i) => i % 2 == 1)
			.Select(l => (long)(JsonDocument.Parse(l).RootElement.GetProperty("Timestamp").GetDateTimeOffset() - DateTimeOffset.UnixEpoch).TotalSeconds)
			.ToArray();

	private static async IAsyncEnumerable<T> Async<T>(IEnumerable<T> items, [EnumeratorCancellation] CancellationToken ct = default)
	{
		foreach (var item in items)
		{
			await Task.Yield();
			ct.ThrowIfCancellationRequested();
			yield return item;
		}
	}

	[Test]
	public async Task ExportsAFiniteSequenceInBatchesAndCompletesExactly()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		using var channel = Channel(t, batch: 10);
		var result = await channel.IngestAllAsync(Documents(25));

		result.Read.Should().Be(25);
		result.Batches.Should().Be(3);
		result.RetriesExhausted.Should().Be(0);
		t.Requests.Select(r => r.Lines.Length / 2).Should().Equal(10, 10, 5);
		channel.InflightEvents.Should().Be(0);
		channel.InflightExportOperations.Should().Be(0);
	}

	[Test]
	public async Task TheChannelStaysUsableForPushAndForMorePulls()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		using var channel = Channel(t, batch: 5);

		(await channel.IngestAllAsync(Documents(5))).Read.Should().Be(5);
		(await channel.IngestAllAsync(Documents(7))).Read.Should().Be(7);

		channel.TryWrite(new TestDocument()).Should().BeTrue("pulling never completes the inbound channel");
		await channel.WaitForDrainAsync(TimeSpan.FromSeconds(5));
		t.Requests.Sum(r => r.Lines.Length / 2).Should().Be(5 + 7 + 1);
	}

	[Test]
	public async Task SingleConcurrencyPreservesSourceOrder()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		using var channel = Channel(t, batch: 7);
		await channel.IngestAllAsync(Documents(50), maxConcurrency: 1);

		t.Requests.SelectMany(Seconds).Should().Equal(Enumerable.Range(0, 50).Select(i => (long)i));
	}

	[Test]
	public async Task ConcurrencyIsBoundedByTheArgument()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		using var channel = Channel(t, batch: 2, concurrency: 8);
		var result = await channel.IngestAllAsync(Documents(100), maxConcurrency: 3);

		result.Batches.Should().Be(50);
		t.MaxInflight.Should().BeLessThanOrEqualTo(3);
		t.Requests.SelectMany(Seconds).OrderBy(s => s).Should().Equal(Enumerable.Range(0, 100).Select(i => (long)i));
	}

	[Test]
	public async Task AsyncSourcesAreSupported()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		using var channel = Channel(t, batch: 10);
		var result = await channel.IngestAllAsync(Async(Documents(23)));

		result.Read.Should().Be(23);
		result.Batches.Should().Be(3);
	}

	[Test]
	public async Task AStalledAsyncSourceDoesNotHoldAPartialBatchHostage()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		using var channel = Channel(t, batch: 100, lifetime: TimeSpan.FromSeconds(1));
		var requestsWhenSourceResumed = -1;

		async IAsyncEnumerable<TestDocument> Slow()
		{
			foreach (var d in Documents(3)) yield return d;
			await Task.Delay(TimeSpan.FromSeconds(2.5));
			requestsWhenSourceResumed = t.Requests.Count;
			yield return new TestDocument();
		}

		var result = await channel.IngestAllAsync(Slow());

		requestsWhenSourceResumed.Should().Be(1, "the three waiting documents were flushed after the lifetime elapsed");
		t.Requests.Select(r => r.Lines.Length / 2).Should().Equal(3, 1);
		result.Read.Should().Be(4);
		result.Batches.Should().Be(2);
	}

	[Test]
	public async Task ItemsThatKeepFailingAreReportedAsRetriesExhausted()
	{
		var t = new ScriptedTransport((_, r) => ScriptedResponse.Items(Enumerable.Repeat(503, ScriptedTransport.CountOperations(r)).ToArray()));
		using var channel = Channel(t, batch: 5, retries: 2);
		var result = await channel.IngestAllAsync(Documents(10));

		result.RetriesExhausted.Should().Be(10);
		t.Requests.Should().HaveCount(2 * 3);
	}

	[Test]
	public async Task RetriesOnlyTheFailedItemsOfABatch()
	{
		var t = new ScriptedTransport((attempt, r) => ScriptedResponse.Items(Enumerable.Range(0, ScriptedTransport.CountOperations(r))
			.Select(i => attempt == 0 && i == 1 ? 503 : 201).ToArray()));
		using var channel = Channel(t, batch: 4, retries: 2);
		var result = await channel.IngestAllAsync(Documents(4));

		result.RetriesExhausted.Should().Be(0);
		t.Requests.Select(r => r.Lines.Length / 2).Should().Equal(4, 1);
		Seconds(t.Requests[1]).Should().Equal(1);
	}

	[Test]
	public async Task CancellationStopsReadingAndLeavesNothingInFlight()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		using var channel = Channel(t, batch: 10);
		using var cts = new CancellationTokenSource();

		IEnumerable<TestDocument> Source()
		{
			for (var i = 0; i < 1000; i++)
			{
				if (i == 25) cts.Cancel();
				yield return new TestDocument();
			}
		}

		var act = () => channel.IngestAllAsync(Source(), ctx: cts.Token);
		await act.Should().ThrowAsync<OperationCanceledException>();
		channel.InflightExportOperations.Should().Be(0);
		t.Requests.Sum(r => r.Lines.Length / 2).Should().BeLessThan(40);
	}

	[Test]
	public async Task MatchesAReferenceModelForRandomSequences()
	{
		await Gen.Select(Gen.Int[0, 150], Gen.Int[1, 20], Gen.Int[1, 4], Gen.Int[1, 4], Gen.Bool).SampleAsync(async x =>
		{
			var (n, batch, channelConcurrency, pullConcurrency, useAsync) = x;
			var t = ScriptedTransport.AlwaysSucceeds();
			using var channel = Channel(t, batch, channelConcurrency);
			var docs = Documents(n);

			var result = useAsync
				? await channel.IngestAllAsync(Async(docs), pullConcurrency)
				: await channel.IngestAllAsync(docs, pullConcurrency);

			var effectiveBatch = channel.BatchExportSize;
			result.Read.Should().Be(n);
			result.Batches.Should().Be((n + effectiveBatch - 1) / effectiveBatch);
			result.RetriesExhausted.Should().Be(0);
			t.MaxInflight.Should().BeLessThanOrEqualTo(pullConcurrency);

			var sent = t.Requests.SelectMany(Seconds).ToArray();
			sent.OrderBy(s => s).Should().Equal(Enumerable.Range(0, n).Select(i => (long)i), "every item is exported exactly once");
			if (pullConcurrency == 1) sent.Should().Equal(Enumerable.Range(0, n).Select(i => (long)i), "one batch at a time keeps order");
		}, iter: 100);
	}
}
