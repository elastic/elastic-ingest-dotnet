// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Mapping;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Orchestration;

/*
 * Tests: IncrementalSync → DeltaSync handoff, including mapping-change rollover.
 *
 *   ┌─────────────────────────────────────────────────────────────────────────┐
 *   │  Phase 1 (IncrementalSyncOrchestrator)                                  │
 *   │    Write 1000 docs                                                       │
 *   │    Strategy: Multiplex (first run, secondary doesn't exist)              │
 *   │    Result: primary=1000, secondary=1000                                  │
 *   │                                                                          │
 *   │  Phase 2 (DeltaSyncOrchestrator, same V1 mapping)                       │
 *   │    Update 150 existing docs + add 150 new docs                           │
 *   │    Strategy: Reindex (secondary already exists, no rollover)             │
 *   │    Result: primary=1150, secondary=1150                                  │
 *   │    Alias still points to original V1 backing index                       │
 *   │                                                                          │
 *   │  Phase 3 (DeltaSyncOrchestrator, V2 mapping → rollover)                 │
 *   │    StartAsync detects hash change → PendingRolloverBackfills non-empty   │
 *   │    BackfillRolledOverIndicesAsync copies 1150 docs → new backing index   │
 *   │    Alias still points to OLD index until CompleteAsync                   │
 *   │    Write 150 more new docs                                               │
 *   │    CompleteAsync (Multiplex) → alias swaps to new V2 backing index       │
 *   │    Result: primary=1300, secondary=1300; both aliases on new V2 index    │
 *   └─────────────────────────────────────────────────────────────────────────┘
 */
[NotInParallel("orchestrator")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class IncrementalToDeltaHandoffTests(IngestionCluster cluster)
	: IntegrationTestBase(cluster)
{
	private static string PrimaryWrite => TestMappingContext.HashableArticleDeltaPrimary.Context.IndexStrategy!.WriteTarget!;
	private static string SecondaryWrite => TestMappingContext.HashableArticleDeltaSecondary.Context.IndexStrategy!.WriteTarget!;
	private static string PrimaryAlias => TestMappingContext.HashableArticleDeltaPrimary.Context.ResolveWriteAlias();
	private static string SecondaryAlias => TestMappingContext.HashableArticleDeltaSecondary.Context.ResolveWriteAlias();

	[Before(Test)]
	public async Task Setup()
	{
		await CleanupPrefixAsync(PrimaryWrite);
		await CleanupPrefixAsync(SecondaryWrite);
	}

	[After(Test)]
	public async Task Teardown()
	{
		await CleanupPrefixAsync(PrimaryWrite);
		await CleanupPrefixAsync(SecondaryWrite);
	}

	[Test]
	public async Task IncrementalToDeltaHandoffWithRollover()
	{
		// ── Phase 1: Seed 1000 docs with IncrementalSyncOrchestrator ────────
		string phase1PrimaryIndex;
		string phase1SecondaryIndex;

		using (var orch1 = new IncrementalSyncOrchestrator<HashableArticle>(
			Transport,
			TestMappingContext.HashableArticleDeltaPrimary,
			TestMappingContext.HashableArticleDeltaSecondary))
		{
			var ctx1 = await orch1.StartAsync(BootstrapMethod.Failure);
			ctx1.Strategy.Should().Be(IngestSyncStrategy.Multiplex,
				"first run: secondary doesn't exist yet → Multiplex");

			for (var i = 0; i < 1000; i++)
				orch1.TryWrite(new HashableArticle { Id = $"doc-{i}", Title = $"Title {i}", Hash = $"hash-{i}" });

			await orch1.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(60));

			phase1PrimaryIndex = await ResolveConcreteIndexAsync(PrimaryAlias);
			phase1SecondaryIndex = await ResolveConcreteIndexAsync(SecondaryAlias);
		}

		await AssertDocCount(PrimaryAlias, 1000, "Phase 1 primary");
		await AssertDocCount(SecondaryAlias, 1000, "Phase 1 secondary");

		// ── Phase 2: Update 150 + add 150 with DeltaSyncOrchestrator (V1) ──
		using (var orch2 = new DeltaSyncOrchestrator<HashableArticle>(
			Transport,
			TestMappingContext.HashableArticleDeltaPrimary,
			TestMappingContext.HashableArticleDeltaSecondary))
		{
			var ctx2 = (DeltaOrchestratorContext<HashableArticle>)await orch2.StartAsync(BootstrapMethod.Failure);
			ctx2.Strategy.Should().Be(IngestSyncStrategy.Reindex,
				"Phase 2: secondary already exists + no mapping change → Reindex");
			ctx2.PendingRolloverBackfills.Should().BeEmpty(
				"mapping unchanged → no rollover → no backfill needed");

			// BackfillRolledOverIndicesAsync is a noop but must be safe to call
			var backfillCount = 0;
			await foreach (var _ in orch2.BackfillRolledOverIndicesAsync())
				backfillCount++;
			backfillCount.Should().Be(0);

			// Update 150 existing docs (IDs 0..149) — different hash triggers reindex-updates
			for (var i = 0; i < 150; i++)
				orch2.TryWrite(new HashableArticle { Id = $"doc-{i}", Title = $"Updated Title {i}", Hash = $"hash-v2-{i}" });

			// Add 150 net-new docs (IDs 1000..1149)
			for (var i = 1000; i < 1150; i++)
				orch2.TryWrite(new HashableArticle { Id = $"doc-{i}", Title = $"Title {i}", Hash = $"hash-{i}" });

			await orch2.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(60));
		}

		await AssertDocCount(PrimaryAlias, 1150, "Phase 2 primary: 1000 original + 150 new (no reaping)");
		await AssertDocCount(SecondaryAlias, 1150, "Phase 2 secondary: reindex-updates propagated 300 changes");

		// Verify no alias rollover happened (same backing indices)
		(await ResolveConcreteIndexAsync(PrimaryAlias)).Should().Be(phase1PrimaryIndex,
			"no mapping change → alias still points to Phase 1 backing index");
		(await ResolveConcreteIndexAsync(SecondaryAlias)).Should().Be(phase1SecondaryIndex);

		// Verify an updated doc has the new content in secondary
		var updatedDoc = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{SecondaryAlias}/_doc/doc-0");
		updatedDoc.ApiCallDetails.HttpStatusCode.Should().Be(200);
		using (var doc = JsonDocument.Parse(updatedDoc.Body))
		{
			var title = doc.RootElement.GetProperty("_source").GetProperty("title").GetString();
			title.Should().Be("Updated Title 0");
		}

		// ── Phase 3: V2 mapping — rollover + backfill + 150 more docs ──────
		using (var orch3 = new DeltaSyncOrchestrator<HashableArticleV2>(
			Transport,
			TestMappingContext.HashableArticleV2DeltaPrimaryV2,
			TestMappingContext.HashableArticleV2DeltaSecondaryV2))
		{
			var ctx3 = (DeltaOrchestratorContext<HashableArticleV2>)await orch3.StartAsync(BootstrapMethod.Failure);
			ctx3.PendingRolloverBackfills.Should().NotBeEmpty(
				"V2 mapping hash differs from V1 → at least one index rolled over → backfill required");
			ctx3.PendingRolloverBackfills.Should().OnlyContain(
				t => t.SourceIndex != t.DestinationIndex, "old and new index must differ");

			// Backfill: copies 1150 docs from old → new primary (and possibly secondary)
			var backfillItems = 0;
			await foreach (var progress in orch3.BackfillRolledOverIndicesAsync())
			{
				backfillItems++;
				progress.Label.Should().NotBeNullOrEmpty();
				progress.SourceIndex.Should().NotBeNullOrEmpty();
				progress.DestinationIndex.Should().NotBeNullOrEmpty();
				progress.DestinationIndex.Should().NotBe(phase1PrimaryIndex,
					"backfill destination must be the new V2 backing index");
			}
			backfillItems.Should().BeGreaterThan(0, "at least one progress item expected during backfill");

			// After backfill, TryWrite should succeed
			for (var i = 1150; i < 1300; i++)
				orch3.TryWrite(new HashableArticleV2 { Id = $"doc-{i}", Title = $"Title {i}", Hash = $"hash-{i}", Tags = ["v2"] });

			await orch3.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(60));
		}

		await AssertDocCount(PrimaryAlias, 1300, "Phase 3 primary: 1150 backfilled + 150 new");
		await AssertDocCount(SecondaryAlias, 1300, "Phase 3 secondary: 1150 backfilled + 150 new");

		// Alias must now point to the new V2 backing index
		var phase3PrimaryIndex = await ResolveConcreteIndexAsync(PrimaryAlias);
		phase3PrimaryIndex.Should().NotBe(phase1PrimaryIndex,
			"alias must have swapped to the new V2 backing index after CompleteAsync");

		// The OLD backing index should still exist (alias swap doesn't delete it)
		var oldIndexHead = await Transport.RequestAsync<StringResponse>(
			HttpMethod.HEAD, $"/{phase1PrimaryIndex}");
		oldIndexHead.ApiCallDetails.HttpStatusCode.Should().Be(200,
			"old backing index persists after alias swap");
	}

	// ── Helpers ─────────────────────────────────────────────────────────

	private async Task AssertDocCount(string alias, long expected, string because)
	{
		await Transport.RequestAsync<StringResponse>(HttpMethod.POST, $"/{alias}/_refresh");
		var count = await Transport.RequestAsync<StringResponse>(HttpMethod.GET, $"/{alias}/_count");
		count.ApiCallDetails.HttpStatusCode.Should().Be(200);
		count.Body.Should().Contain($"\"count\":{expected}",
			$"{because}: {alias} expected {expected} docs, got: {count.Body}");
	}

	private async Task<string> ResolveConcreteIndexAsync(string alias)
	{
		var rq = new RequestConfiguration { Accept = "text/plain" };
		var response = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"_cat/aliases/{alias}?h=index", null, rq, default);
		var index = response.Body?.Trim(Environment.NewLine.ToCharArray());
		index.Should().NotBeNullOrEmpty($"alias {alias} should resolve to a concrete index");
		return index!;
	}
}
