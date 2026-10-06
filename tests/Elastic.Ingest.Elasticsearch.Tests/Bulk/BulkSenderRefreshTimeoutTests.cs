// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CsCheck;
using Elastic.Ingest.Elasticsearch.Bulk;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests.Bulk;

/// <summary>The explicit <c>Refresh</c> and <c>RequestTimeout</c> options of a <see cref="BulkSender{TItem,TBody}"/> (#214).</summary>
public class BulkSenderRefreshTimeoutTests
{
	private const string FilterPath = "filter_path=errors,error,items.*.status,items.*.error,items.*.result,items.*._version";

	private static Doc[] Docs(int n) => Enumerable.Range(0, n).Select(i => new Doc($"id{i}", "n", i)).ToArray();

	private static BulkSender<Doc, Doc> Sender(ScriptedTransport t, string target = null, BulkRefresh? refresh = null, TimeSpan? timeout = null, BulkRetryPolicy retry = null) =>
		new(new BulkSenderOptions<Doc, Doc>
		{
			Transport = t.Transport,
			BodyTypeInfo = BulkTestContext.Default.Doc,
			Action = static d => BulkAction.Index(d.Id),
			Body = static d => d,
			Target = target,
			Refresh = refresh,
			RequestTimeout = timeout,
			Retry = retry ?? BulkRetryPolicy.None
		});

	[Test]
	public async Task WithoutOptionsTheRequestIsExactlyWhatItAlwaysWas()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		await Sender(t).SendAsync(Docs(1));

		t.Requests.Single().PathAndQuery.Should().Be("_bulk?" + FilterPath);
	}

	[Test]
	[Arguments(BulkRefresh.True, "refresh=true")]
	[Arguments(BulkRefresh.False, "refresh=false")]
	[Arguments(BulkRefresh.WaitFor, "refresh=wait_for")]
	public async Task RefreshBecomesTheFirstQueryParameterAndFilterPathIsKept(BulkRefresh refresh, string expected)
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		await Sender(t, refresh: refresh).SendAsync(Docs(1));

		t.Requests.Single().PathAndQuery.Should().Be("_bulk?" + expected + "&" + FilterPath);
	}

	[Test]
	public async Task RefreshCombinesWithATarget()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		await Sender(t, target: "/products/", refresh: BulkRefresh.WaitFor).SendAsync(Docs(1));

		t.Requests.Single().PathAndQuery.Should().Be("products/_bulk?refresh=wait_for&" + FilterPath);
	}

	[Test]
	public void AnUnknownRefreshValueIsRejectedWhenTheSenderIsCreated()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		Action act = () => Sender(t, refresh: (BulkRefresh)99);
		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Test]
	public async Task EveryRequestCarriesTheConfiguredTimeoutIncludingRetriesAndBatches()
	{
		var t = new ScriptedTransport((attempt, r) => ScriptedResponse.Items(ScriptedTransport.IdsOf(r).Select(_ => attempt == 0 ? 503 : 201).ToArray()));
		var sender = Sender(t, timeout: TimeSpan.FromSeconds(7), refresh: BulkRefresh.WaitFor, retry: BulkRetryPolicy.Default with { Backoff = static _ => TimeSpan.Zero });

		await sender.IngestAllAsync(Docs(25), new IngestAllOptions { BatchSize = 10, MaxConcurrency = 1 });

		t.Requests.Should().HaveCountGreaterThan(3, "the first attempt of each batch failed and was retried");
		t.Requests.All(r => r.RequestTimeout == TimeSpan.FromSeconds(7)).Should().BeTrue();
		t.Requests.All(r => r.PathAndQuery == "_bulk?refresh=wait_for&" + FilterPath).Should().BeTrue();
	}

	[Test]
	public async Task WithoutATimeoutTheTransportDefaultApplies()
	{
		var baseline = ScriptedTransport.AlwaysSucceeds();
		await Sender(baseline).SendAsync(Docs(1));
		var transportDefault = baseline.Requests.Single().RequestTimeout;

		var t = ScriptedTransport.AlwaysSucceeds();
		await Sender(t, timeout: TimeSpan.FromSeconds(3)).SendAsync(Docs(1));

		transportDefault.Should().NotBe(TimeSpan.FromSeconds(3));
		t.Requests.Single().RequestTimeout.Should().Be(TimeSpan.FromSeconds(3), "the sender's timeout overrides the transport's");
	}

	[Test]
	public async Task AnInfiniteTimeoutIsAccepted()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		await Sender(t, timeout: Timeout.InfiniteTimeSpan).SendAsync(Docs(1));
		t.Requests.Should().ContainSingle();
	}

	[Test]
	[Arguments(0)]
	[Arguments(-1000)]
	[Arguments(-2)]
	public void ANonPositiveTimeoutIsRejectedWhenTheSenderIsCreated(int milliseconds)
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		Action act = () => Sender(t, timeout: TimeSpan.FromMilliseconds(milliseconds));
		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Test]
	public async Task TheCreateHelperPassesBothOptionsThrough()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var sender = BulkSender.Create(t.Transport, BulkTestContext.Default.Doc, static d => BulkAction.Index(d.Id), target: "p",
			refresh: BulkRefresh.True, requestTimeout: TimeSpan.FromSeconds(11));
		await sender.SendAsync(Docs(1));

		var request = t.Requests.Single();
		request.PathAndQuery.Should().Be("p/_bulk?refresh=true&" + FilterPath);
		request.RequestTimeout.Should().Be(TimeSpan.FromSeconds(11));
	}

	[Test]
	public async Task TheBodyDoesNotDependOnTheseOptions()
	{
		var plain = ScriptedTransport.AlwaysSucceeds();
		var tuned = ScriptedTransport.AlwaysSucceeds();
		await Sender(plain).SendAsync(Docs(5));
		await Sender(tuned, refresh: BulkRefresh.WaitFor, timeout: TimeSpan.FromSeconds(1)).SendAsync(Docs(5));

		tuned.Requests.Single().Body.Should().Equal(plain.Requests.Single().Body);
	}

	// ---- properties ----

	private static readonly Gen<string> Targets = Gen.OneOfConst<string>(null, "", "  ", "a", "/a/", "a/b", "logs-*", "//x//", "my-index-000001");
	private static readonly Gen<BulkRefresh?> Refreshes = Gen.OneOfConst<BulkRefresh?>(null, BulkRefresh.False, BulkRefresh.True, BulkRefresh.WaitFor);

	[Test]
	public async Task TheUrlIsWellFormedForEveryCombinationOfTargetAndRefresh()
	{
		await Gen.Select(Targets, Refreshes).SampleAsync(async x =>
		{
			var (target, refresh) = x;
			var t = ScriptedTransport.AlwaysSucceeds();
			await Sender(t, target, refresh).SendAsync(Docs(1));
			var request = t.Requests.Single();

			var expectedPath = string.IsNullOrWhiteSpace(target) ? "_bulk" : target.Trim('/') + "/_bulk";
			request.Path.Should().Be(expectedPath);

			var query = request.Query;
			query.Count(q => q.StartsWith("filter_path=", StringComparison.Ordinal)).Should().Be(1);
			query.Single(q => q.StartsWith("filter_path=", StringComparison.Ordinal)).Should().Be(FilterPath, "the built in filter_path is never altered");

			var refreshParameters = query.Where(q => q.StartsWith("refresh=", StringComparison.Ordinal)).ToArray();
			if (refresh is null)
			{
				refreshParameters.Should().BeEmpty();
				query.Should().HaveCount(1);
			}
			else
			{
				refreshParameters.Should().Equal(refresh switch
				{
					BulkRefresh.True => "refresh=true",
					BulkRefresh.False => "refresh=false",
					_ => "refresh=wait_for"
				});
				query.Should().HaveCount(2);
				query[0].Should().Be(refreshParameters[0]);
			}
		}, iter: 200);
	}

	[Test]
	public async Task EveryRequestOfARunSharesTheSameUrlAndTimeoutWhateverHappens()
	{
		var timeouts = Gen.Select(Gen.Bool, Gen.Int[1, 3_600_000], (has, ms) => has ? (TimeSpan?)TimeSpan.FromMilliseconds(ms) : null);
		var gen = Gen.Select(Gen.Int[0, 60], Gen.Int[1, 12], Gen.Int[1, 3], Gen.Int[0, 3], Refreshes, timeouts);

		await gen.SampleAsync(async x =>
		{
			var (n, batch, concurrency, retries, refresh, timeout) = x;
			// every first attempt of an item fails transiently so retries add more requests
			var t = new ScriptedTransport((attempt, r) => ScriptedResponse.Items(ScriptedTransport.IdsOf(r).Select(_ => attempt % 2 == 0 ? 503 : 201).ToArray()));
			var sender = Sender(t, "p", refresh, timeout, BulkRetryPolicy.Default with { MaxRetries = retries, Backoff = static _ => TimeSpan.Zero });

			await sender.IngestAllAsync(Docs(n), new IngestAllOptions { BatchSize = batch, MaxConcurrency = concurrency });

			var baseline = ScriptedTransport.AlwaysSucceeds();
			await Sender(baseline).SendAsync(Docs(1));
			var expectedTimeout = timeout ?? baseline.Requests.Single().RequestTimeout;

			t.Requests.Select(r => r.PathAndQuery).Distinct().Should().HaveCountLessThanOrEqualTo(1);
			t.Requests.All(r => r.RequestTimeout == expectedTimeout).Should().BeTrue();
		}, iter: 100);
	}
}
