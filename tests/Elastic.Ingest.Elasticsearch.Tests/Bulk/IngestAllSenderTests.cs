// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Elastic.Ingest.Elasticsearch.Bulk;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests.Bulk;

public class IngestAllSenderTests
{
	private static Doc[] Docs(int n) => Enumerable.Range(0, n).Select(i => new Doc($"id{i}", "n", i)).ToArray();

	private static IngestAllOptions Fast(int batch = 1000, int concurrency = 1) => new()
	{
		BatchSize = batch,
		MaxConcurrency = concurrency,
		Retry = BulkRetryPolicy.Default with { Backoff = static _ => TimeSpan.Zero }
	};

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
	public async Task StaticHelperStoresAListWithDefaultIndexAction()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var result = await BulkSender.IngestAllAsync(t.Transport, BulkTestContext.Default.Doc, Docs(5), "products");

		result.Read.Should().Be(5);
		result.Batches.Should().Be(1);
		result.Failures.Should().BeEmpty();
		result.Succeeded.Should().Be(5);
		t.Requests.Single().PathAndQuery.Should().StartWith("products/_bulk?");
		t.Requests.Single().Lines.Where((_, i) => i % 2 == 0).Should().OnlyContain(l => l == "{\"index\":{}}");
	}

	[Test]
	public async Task EmptySourceSendsNothing()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var result = await BulkSender.IngestAllAsync(t.Transport, BulkTestContext.Default.Doc, Array.Empty<Doc>(), "p");

		result.Read.Should().Be(0);
		result.Batches.Should().Be(0);
		t.Requests.Should().BeEmpty();
	}

	[Test]
	public async Task SlicesIntoBatchesInOrderWhenConcurrencyIsOne()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var result = await BulkSender.IngestAllAsync(t.Transport, BulkTestContext.Default.Doc, Docs(2500), "p",
			static d => BulkAction.Index(d.Id), Fast());

		result.Batches.Should().Be(3);
		t.Requests.Select(r => r.Lines.Length / 2).Should().Equal(1000, 1000, 500);
		t.Requests.SelectMany(ScriptedTransport.IdsOf).Should().Equal(Docs(2500).Select(d => d.Id));
	}

	[Test]
	public async Task LazyAndListSourcesBehaveLikeArrays()
	{
		foreach (var source in new IEnumerable<Doc>[] { Docs(35).ToList(), Docs(35).Select(d => d), Docs(35).Where(_ => true) })
		{
			var t = ScriptedTransport.AlwaysSucceeds();
			var result = await BulkSender.IngestAllAsync(t.Transport, BulkTestContext.Default.Doc, source, "p", static d => BulkAction.Index(d.Id), Fast(10));

			result.Read.Should().Be(35);
			t.Requests.Select(r => r.Lines.Length / 2).Should().Equal(10, 10, 10, 5);
		}
	}

	[Test]
	public async Task AsyncSourceIsBatchedToo()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var result = await BulkSender.IngestAllAsync(t.Transport, BulkTestContext.Default.Doc, Async(Docs(25)), "p", static d => BulkAction.Index(d.Id), Fast(10));

		result.Read.Should().Be(25);
		result.Batches.Should().Be(3);
		t.Requests.SelectMany(ScriptedTransport.IdsOf).Should().Equal(Docs(25).Select(d => d.Id));
	}

	[Test]
	public async Task FailuresCarryTheirSourcePosition()
	{
		var t = new ScriptedTransport((_, r) => ScriptedResponse.Items(ScriptedTransport.IdsOf(r).Select(id => id is "id3" or "id12" ? 400 : 201).ToArray()));
		var result = await BulkSender.IngestAllAsync(t.Transport, BulkTestContext.Default.Doc, Docs(15), "p", static d => BulkAction.Index(d.Id), Fast(10));

		result.Failures.Select(f => f.Position).Should().Equal(3, 12);
		result.Failures.Should().OnlyContain(f => f.Item.Status == 400 && f.Item.Error != null);
		result.Succeeded.Should().Be(13);
		result.RetriesExhausted.Should().Be(2);
	}

	[Test]
	public async Task RetriesFailedItemsByDefault()
	{
		var t = new ScriptedTransport((attempt, r) => ScriptedResponse.Items(ScriptedTransport.IdsOf(r).Select(id => attempt == 0 && id == "id1" ? 503 : 201).ToArray()));
		var result = await BulkSender.IngestAllAsync(t.Transport, BulkTestContext.Default.Doc, Docs(3), "p", static d => BulkAction.Index(d.Id), Fast());

		t.Requests.Should().HaveCount(2);
		result.Failures.Should().BeEmpty();
	}

	[Test]
	public async Task AnHttpFailureMarksTheWholeBatchFailed()
	{
		var t = new ScriptedTransport((_, _) => ScriptedResponse.Http(500));
		var result = await BulkSender.IngestAllAsync(t.Transport, BulkTestContext.Default.Doc, Docs(4), "p", static d => BulkAction.Index(d.Id), Fast());

		result.Failures.Select(f => f.Position).Should().Equal(0, 1, 2, 3);
		result.Failures.Should().OnlyContain(f => f.Item.Status == 500);
	}

	[Test]
	public async Task ConcurrencyIsBounded()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var result = await BulkSender.IngestAllAsync(t.Transport, BulkTestContext.Default.Doc, Docs(400), "p", static d => BulkAction.Index(d.Id), Fast(10, 3));

		result.Batches.Should().Be(40);
		t.MaxInflight.Should().BeLessThanOrEqualTo(3);
		t.Requests.SelectMany(ScriptedTransport.IdsOf).Should().BeEquivalentTo(Docs(400).Select(d => d.Id));
	}

	[Test]
	public async Task CancellationStopsReadingAndSettlesInflightBatches()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		using var cts = new CancellationTokenSource();
		var read = 0;
		IEnumerable<Doc> Source()
		{
			for (var i = 0; i < 1000; i++)
			{
				if (i == 25) cts.Cancel();
				read++;
				yield return new Doc($"id{i}", "n", i);
			}
		}

		var act = () => BulkSender.IngestAllAsync(t.Transport, BulkTestContext.Default.Doc, Source(), "p", static d => BulkAction.Index(d.Id), Fast(10), cts.Token);
		await act.Should().ThrowAsync<OperationCanceledException>();
		read.Should().BeLessThan(40);
	}

	[Test]
	public async Task MatchesAReferenceModelForRandomSourcesBatchesAndFailures()
	{
		var gen = Gen.Select(Gen.Int[0, 120], Gen.Int[1, 25], Gen.Int[1, 4], Gen.Int[0, 3])
			.SelectMany(x => Gen.Bool.Array[x.Item1].Select(fail => (n: x.Item1, batch: x.Item2, concurrency: x.Item3, kind: x.Item4, fail)));

		await gen.SampleAsync(async x =>
		{
			var docs = Docs(x.n);
			var t = new ScriptedTransport((_, r) => ScriptedResponse.Items(ScriptedTransport.IdsOf(r)
				.Select(id => x.fail[int.Parse(id[2..], System.Globalization.CultureInfo.InvariantCulture)] ? 400 : 201).ToArray()));
			var options = Fast(x.batch, x.concurrency);
			Func<Doc, BulkAction> action = static d => BulkAction.Index(d.Id);

			var result = x.kind switch
			{
				0 => await BulkSender.IngestAllAsync(t.Transport, BulkTestContext.Default.Doc, docs, "p", action, options),
				1 => await BulkSender.IngestAllAsync(t.Transport, BulkTestContext.Default.Doc, docs.ToList(), "p", action, options),
				2 => await BulkSender.IngestAllAsync(t.Transport, BulkTestContext.Default.Doc, docs.Select(d => d), "p", action, options),
				_ => await BulkSender.IngestAllAsync(t.Transport, BulkTestContext.Default.Doc, Async(docs), "p", action, options)
			};

			result.Read.Should().Be(x.n);
			result.Batches.Should().Be((x.n + x.batch - 1) / x.batch);
			result.Failures.Select(f => f.Position).Should().Equal(Enumerable.Range(0, x.n).Where(i => x.fail[i]).Select(i => (long)i));

			var sent = t.Requests.SelectMany(ScriptedTransport.IdsOf).ToArray();
			sent.Should().HaveCount(x.n, "failed items are not retryable and nothing is sent twice");
			sent.Distinct().Should().HaveCount(x.n);
			t.Requests.All(r => r.Lines.Length / 2 <= x.batch).Should().BeTrue();
			t.MaxInflight.Should().BeLessThanOrEqualTo(x.concurrency);
			if (x.concurrency == 1) sent.Should().Equal(docs.Select(d => d.Id), "a single request in flight keeps order");
		}, iter: 150);
	}
}
