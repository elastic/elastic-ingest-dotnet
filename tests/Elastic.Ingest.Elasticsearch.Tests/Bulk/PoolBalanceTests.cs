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

/// <summary>
/// Whatever goes wrong (transport exceptions, HTTP errors, serialization failures, cancellation, a failing source)
/// every array rented from a pool is returned exactly once and no request outlives the call.
/// </summary>
public class PoolBalanceTests
{
	private sealed class Pools
	{
		public TrackingArrayPool<byte> Bytes { get; } = new();
		public TrackingArrayPool<int> Ints { get; } = new();
		public TrackingArrayPool<Doc> Items { get; } = new();

		public void AssertBalanced()
		{
			Bytes.AssertBalanced();
			Ints.AssertBalanced();
			Items.AssertBalanced();
		}

		public BulkSender<Doc, Doc> Sender(ScriptedTransport t, BulkRetryPolicy retry, Func<Doc, Doc> body = null) =>
			new(new BulkSenderOptions<Doc, Doc>
			{
				Transport = t.Transport,
				BodyTypeInfo = BulkTestContext.Default.Doc,
				Action = static d => BulkAction.Index(d.Id),
				Body = body ?? (static d => d),
				Retry = retry
			}, Bytes, Ints, Items);
	}

	// per attempt: 0 = items with random statuses, 1 = HTTP 500, 2 = HTTP 429, 3 = the transport throws
	private static ScriptedResponse Outcome(int kind, CapturedRequest request, Func<int> status) => kind switch
	{
		1 => ScriptedResponse.Http(500),
		2 => ScriptedResponse.Http(429),
		3 => new ScriptedResponse(200, Throw: new InvalidOperationException("transport failed")),
		_ => ScriptedResponse.Items(ScriptedTransport.IdsOf(request).Select(_ => status()).ToArray())
	};

	private static async Task SwallowAsync(Func<Task> act)
	{
		try { await act().ConfigureAwait(false); }
		catch (Exception) { /* the point is what is left behind */ }
	}

	[Test]
	public async Task SendAsyncReturnsEverythingItRentedWhateverHappens()
	{
		var statuses = Gen.OneOfConst(201, 201, 400, 429, 503);
		var gen = Gen.Select(Gen.Int[0, 40], Gen.Int[0, 3], Gen.Int[-1, 40], Gen.Int[-1, 3])
			.SelectMany(x => Gen.Select(Gen.Int[0, 3].Array[4], statuses.Array[200], (kinds, flat) => (x.Item1, x.Item2, throwAt: x.Item3, cancelAt: x.Item4, kinds, flat)));

		await gen.SampleAsync(async x =>
		{
			var (n, retries, throwAt, cancelAt, kinds, flat) = x;
			var pools = new Pools();
			using var cts = new CancellationTokenSource();
			var next = 0;
			var t = new ScriptedTransport((attempt, r) =>
			{
				if (attempt == cancelAt) cts.Cancel();
				return Outcome(kinds[Math.Min(attempt, 3)], r, () => flat[next++ % flat.Length]);
			});
			var policy = BulkRetryPolicy.Default with { MaxRetries = retries, Backoff = static _ => TimeSpan.FromMilliseconds(1) };
			var index = 0;
			var sender = pools.Sender(t, policy, d => index++ == throwAt ? throw new FormatException("bad body") : d);
			var docs = Enumerable.Range(0, n).Select(i => new Doc($"id{i}", "n", i)).ToArray();

			await SwallowAsync(() => sender.SendAsync(docs, cts.Token));

			pools.AssertBalanced();
			t.Inflight.Should().Be(0);
		}, iter: 400);
	}

	[Test]
	public async Task SendAsyncOverASpanOrAnEnumerableBalancesToo()
	{
		var pools = new Pools();
		var t = ScriptedTransport.AlwaysSucceeds();
		var sender = pools.Sender(t, BulkRetryPolicy.None);
		var docs = Enumerable.Range(0, 2000).Select(i => new Doc($"id{i}", new string('x', 500), i)).ToArray();

		await sender.SendAsync(docs.AsSpan());
		await sender.SendAsync(docs.ToList());
		await sender.SendAsync(docs.Select(d => d));
		await sender.SendAsync(Array.Empty<Doc>());

		pools.AssertBalanced();
		pools.Bytes.Rents.Should().BeGreaterThan(0);
	}

	private static async IAsyncEnumerable<Doc> AsyncDocs(int n, int failAt, Action<int> onItem, [EnumeratorCancellation] CancellationToken ct = default)
	{
		for (var i = 0; i < n; i++)
		{
			await Task.Yield();
			ct.ThrowIfCancellationRequested();
			onItem(i);
			if (i == failAt) throw new InvalidOperationException("source failed");
			yield return new Doc($"id{i}", "n", i);
		}
	}

	private static IEnumerable<Doc> LazyDocs(int n, int failAt, Action<int> onItem)
	{
		for (var i = 0; i < n; i++)
		{
			onItem(i);
			if (i == failAt) throw new InvalidOperationException("source failed");
			yield return new Doc($"id{i}", "n", i);
		}
	}

	[Test]
	public async Task IngestAllReturnsEverythingItRentedAndLeavesNothingRunning()
	{
		var statuses = Gen.OneOfConst(201, 201, 400, 429, 503);
		var gen = Gen.Select(Gen.Int[0, 60], Gen.Int[1, 10], Gen.Int[1, 4], Gen.Int[0, 2])
			.SelectMany(x => Gen.Select(Gen.Int[-1, 60], Gen.Int[-1, 60], Gen.Int[-1, 4], statuses.Array[300], (failAt, cancelAt, throwAttempt, flat) => (x.Item1, x.Item2, x.Item3, x.Item4, failAt, cancelAt, throwAttempt, flat)));

		await gen.SampleAsync(async x =>
		{
			var (n, batch, conc, sourceKind, failAt, cancelAt, throwAttempt, flat) = x;
			var pools = new Pools();
			using var cts = new CancellationTokenSource();
			var next = 0;
			var t = new ScriptedTransport((attempt, r) => attempt == throwAttempt
				? new ScriptedResponse(200, Throw: new InvalidOperationException("transport failed"))
				: ScriptedResponse.Items(ScriptedTransport.IdsOf(r).Select(_ => flat[next++ % flat.Length]).ToArray()));
			var sender = pools.Sender(t, BulkRetryPolicy.Default with { MaxRetries = 2, Backoff = static _ => TimeSpan.Zero });
			var options = new IngestAllOptions { BatchSize = batch, MaxConcurrency = conc };

			void OnItem(int i) { if (i == cancelAt) cts.Cancel(); }
			await SwallowAsync(() => sourceKind switch
			{
				0 => sender.IngestAllAsync(LazyDocs(n, failAt, OnItem), options, cts.Token),
				1 => sender.IngestAllAsync(AsyncDocs(n, failAt, OnItem), options, cts.Token),
				_ => sender.IngestAllAsync(LazyDocs(n, failAt, OnItem).ToArray(), options, cts.Token)
			});

			pools.AssertBalanced();
			t.Inflight.Should().Be(0, "an aborted call waits for the requests it started");
		}, iter: 400);
	}
}
