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
 * Tests: IncrementalSyncOrchestrator — two-run incremental sync
 *
 * Validates that the orchestrator:
 *   1. First run (secondary missing)  → Multiplex, both indices populated
 *   2. Second run (secondary exists)  → Reindex, only primary receives writes,
 *      changed documents are reindexed to secondary, stale documents deleted
 *
 * Uses HashableArticle with [ContentHash] so unchanged documents are NOOPed
 * (only batch_index_date is updated via the orchestrator's hash-match script).
 * The Reindex step copies docs whose last_updated >= max(batch_index_date) from
 * the secondary, meaning only truly changed documents touch the secondary.
 *
 * The orchestrator auto-stamps batch_index_date and last_updated on each document
 * via the IStaticMappingResolver<T> setter delegates — callers never set them.
 *
 * Primary and secondary targets are defined as Entity<HashableArticle> registrations
 * (Variant = "Primary" / "Secondary") in TestMappingContext, so the orchestrator
 * derives everything from the resolvers.
 *
 *   ┌───────────────────────────────────────────────────────────────────┐
 *   │  Run 1: Write 10 docs → Multiplex → both have 10 docs             │
 *   │  Run 2: Write 7 docs  → Reindex   → primary has 7, secondary 7    │
 *   │         5 unchanged (NOOP on primary, not reindexed to secondary) │
 *   │         2 modified  (updated on primary, reindexed to secondary)  │
 *   │         3 stale     (deleted from both via batch_index_date)      │
 *   └───────────────────────────────────────────────────────────────────┘
 */
[NotInParallel("orchestrator")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class IncrementalSyncOrchestratorTests(IngestionCluster cluster)
	: IntegrationTestBase(cluster)
{
	private static string WriteTarget(IStaticMappingResolver<HashableArticle> r) =>
		r.Context.IndexStrategy!.WriteTarget!;

	private static string WriteAlias(IStaticMappingResolver<HashableArticle> r) =>
		TypeContextResolver.ResolveWriteAlias(r.Context);

	private static readonly string PrimaryAlias = WriteAlias(TestMappingContext.HashableArticlePrimary);
	private static readonly string SecondaryAlias = WriteAlias(TestMappingContext.HashableArticleSecondary);

	private static readonly string PrimaryWrite = WriteTarget(TestMappingContext.HashableArticlePrimary);
	private static readonly string SecondaryWrite = WriteTarget(TestMappingContext.HashableArticleSecondary);


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
	public async Task SecondRunUsesReindexAndSyncsCorrectly()
	{
		// ── Run 1: secondary doesn't exist → Multiplex ──────────────────
		using var orch1 = new IncrementalSyncOrchestrator<HashableArticle>(
			Transport, TestMappingContext.HashableArticlePrimary, TestMappingContext.HashableArticleSecondary
		);

		var strategy1 = await orch1.StartAsync(BootstrapMethod.Failure);
		strategy1.Should().Be(IngestSyncStrategy.Multiplex,
			"first run should Multiplex because the secondary index does not exist yet");

		for (var i = 0; i < 10; i++)
		{
			orch1.TryWrite(new HashableArticle
			{
				Id = $"doc-{i}",
				Title = $"Original title {i}",
				Hash = $"hash-{i}",
			});
		}

		var completed1 = await orch1.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(30));
		completed1.Should().BeTrue("first sync should complete successfully");

		await AssertDocCount(PrimaryAlias, 10);
		await AssertDocCount(SecondaryWrite, 10);

		await AssertDocCount(PrimaryWrite, 10);
		await AssertDocCount(PrimaryWrite, 10);

		// ── Run 2: secondary exists → Reindex ───────────────────────────
		using var orch2 = new IncrementalSyncOrchestrator<HashableArticle>(
			Transport, TestMappingContext.HashableArticlePrimary, TestMappingContext.HashableArticleSecondary
		);

		var strategy2 = await orch2.StartAsync(BootstrapMethod.Failure);
		strategy2.Should().Be(IngestSyncStrategy.Reindex,
			"second run should Reindex because the secondary index already exists");

		for (var i = 0; i < 7; i++)
		{
			var title = i < 5 ? $"Original title {i}" : $"Modified title {i}";
			orch2.TryWrite(new HashableArticle
			{
				Id = $"doc-{i}",
				Title = title,
				Hash = $"hash-{i}",
			});
		}

		var completed2 = await orch2.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(30));
		completed2.Should().BeTrue("second sync should complete successfully");

		// doc-7, doc-8, doc-9 were not re-sent → stale → deleted from both
		await AssertDocCount(PrimaryAlias, 7);
		await AssertDocCount(SecondaryAlias, 7);

		// Verify a modified document was actually updated in the secondary
		var docResponse = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{SecondaryAlias}/_doc/doc-6");
		docResponse.ApiCallDetails.HttpStatusCode.Should().Be(200);
		using var doc = JsonDocument.Parse(docResponse.Body);
		var title6 = doc.RootElement.GetProperty("_source").GetProperty("title").GetString();
		title6.Should().Be("Modified title 6");
	}

	private async Task AssertDocCount(string alias, long expected)
	{
		await Transport.RequestAsync<StringResponse>(HttpMethod.POST, $"/{alias}/_refresh");

		var count = await Transport.RequestAsync<StringResponse>(HttpMethod.GET, $"/{alias}/_count");
		count.ApiCallDetails.HttpStatusCode.Should().Be(200, $"count on {alias} should succeed");
		count.Body.Should().Contain($"\"count\":{expected}",
			$"{alias} should contain {expected} documents but got: {count.Body}");
	}
}
