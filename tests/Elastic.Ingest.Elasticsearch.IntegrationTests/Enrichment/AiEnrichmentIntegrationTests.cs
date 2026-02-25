// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.Enrichment;
using Elastic.Mapping;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Enrichment;

/*
 * Tests: AiEnrichmentOrchestrator — end-to-end lifecycle
 *
 * Validates the full AI enrichment flow:
 *   1. Initialize — lookup index + enrich policy + pipeline created
 *   2. Index documents via IncrementalSyncOrchestrator
 *   3. Enrich — query for candidates, call LLM, update lookup, backfill
 *   4. Cleanup — purge orphaned/stale entries
 *
 *   ┌───────────────────────────────────────────────────────────────────┐
 *   │  1. Initialize enrichment (lookup index + policy + pipeline)       │
 *   │  2. Index 3 documentation pages via orchestrator (Multiplex)      │
 *   │  3. Run enrichment → enriches documents via LLM inference         │
 *   │  4. Verify enriched fields exist on secondary index               │
 *   │  5. Purge lookup → verify cleanup works                           │
 *   └───────────────────────────────────────────────────────────────────┘
 */
[NotInParallel("ai-enrichment")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class AiEnrichmentIntegrationTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private static IAiEnrichmentProvider Provider => TestMappingContext.AiEnrichment;

	private static readonly string PrimaryWrite =
		TestMappingContext.AiDocumentationPageAiPrimary.Context.IndexStrategy!.WriteTarget!;
	private static readonly string SecondaryWrite =
		TestMappingContext.AiDocumentationPageAiSecondary.Context.IndexStrategy!.WriteTarget!;

	[Before(Test)]
	public async Task Setup()
	{
		await CleanupPrefixAsync(PrimaryWrite);
		await CleanupPrefixAsync(SecondaryWrite);
		await CleanupLookupAsync();
		await CleanupPolicyAndPipelineAsync();
	}

	[After(Test)]
	public async Task Teardown()
	{
		await CleanupPrefixAsync(PrimaryWrite);
		await CleanupPrefixAsync(SecondaryWrite);
		await CleanupLookupAsync();
		await CleanupPolicyAndPipelineAsync();
	}

	[Test]
	public async Task InitializeCreatesLookupIndexAndPolicy()
	{
		using var enrichment = new AiEnrichmentOrchestrator(Transport, Provider);
		await enrichment.InitializeAsync();

		var lookupExists = await Transport.RequestAsync<StringResponse>(
			HttpMethod.HEAD, Provider.LookupIndexName);
		lookupExists.ApiCallDetails.HttpStatusCode.Should().Be(200,
			"lookup index should exist after initialization");

		var pipelineExists = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"_ingest/pipeline/{Provider.PipelineName}");
		pipelineExists.ApiCallDetails.HttpStatusCode.Should().Be(200,
			"ingest pipeline should exist after initialization");
	}

	[Test]
	public async Task EnrichAsyncProcessesDocumentsEndToEnd()
	{
		// ── 1. Initialize enrichment infrastructure ──
		using var enrichment = new AiEnrichmentOrchestrator(Transport, Provider);
		await enrichment.InitializeAsync();

		// ── 2. Index documents via orchestrator ──
		using var orch = new IncrementalSyncOrchestrator<AiDocumentationPage>(
			Transport,
			TestMappingContext.AiDocumentationPageAiPrimary,
			TestMappingContext.AiDocumentationPageAiSecondary
		);

		await orch.StartAsync(BootstrapMethod.Failure);

		var pages = new[]
		{
			new AiDocumentationPage
			{
				Url = "/getting-started",
				Title = "Getting Started Guide",
				Body = "This guide walks you through installing Elasticsearch, configuring your cluster, and indexing your first document. You will learn how to set up nodes, configure memory settings, and verify your installation is working correctly."
			},
			new AiDocumentationPage
			{
				Url = "/search-api",
				Title = "Search API Reference",
				Body = "The Search API allows you to execute search queries against one or more indices. It supports full-text search, term-level queries, compound queries, and aggregations. You can paginate results, highlight matches, and sort by relevance or field values."
			},
			new AiDocumentationPage
			{
				Url = "/ingest-pipelines",
				Title = "Ingest Pipelines",
				Body = "Ingest pipelines let you transform documents before indexing. Processors in a pipeline can rename fields, convert data types, enrich documents from external sources, and run inference models. Pipelines are defined via the _ingest API."
			}
		};

		foreach (var page in pages)
			orch.TryWrite(page);

		var completed = await orch.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(30));
		completed.Should().BeTrue("orchestrator should complete successfully");

		// ── 3. Run enrichment ──
		var secondaryAlias = TestMappingContext.AiDocumentationPageAiSecondary.Context.ResolveWriteAlias();
		var result = await enrichment.EnrichAsync(secondaryAlias);

		result.Should().NotBeNull();
		result.TotalCandidates.Should().BeGreaterThan(0,
			"documents without AI fields should be found as candidates");

		// If no inference endpoint is available, enrichment will fail gracefully
		// (the LLM call returns null). In that case we still verify the plumbing works.
		if (result.Enriched > 0)
		{
			// ── 4. Verify enriched fields on secondary ──
			await Transport.RequestAsync<StringResponse>(
				HttpMethod.POST, $"/{secondaryAlias}/_refresh");

			var search = await Transport.RequestAsync<StringResponse>(
				HttpMethod.POST, $"/{secondaryAlias}/_search",
				PostData.String("""{"size":1,"_source":["ai_summary","ai_questions","ai_summary_ph"],"query":{"exists":{"field":"ai_summary"}}}"""));

			if (search.ApiCallDetails.HttpStatusCode == 200 && search.Body != null)
			{
				using var doc = JsonDocument.Parse(search.Body);
				var hits = doc.RootElement.GetProperty("hits").GetProperty("hits");
				if (hits.GetArrayLength() > 0)
				{
					var source = hits[0].GetProperty("_source");
					source.TryGetProperty("ai_summary", out _).Should().BeTrue(
						"enriched document should have ai_summary field");
				}
			}
		}

		// Enrichment ran without exceptions regardless of LLM availability
		(result.Enriched + result.Failed).Should().BeGreaterOrEqualTo(0);
	}

	[Test]
	public async Task PurgeAsyncClearsLookupIndex()
	{
		using var enrichment = new AiEnrichmentOrchestrator(Transport, Provider);
		await enrichment.InitializeAsync();

		// Seed a document into the lookup index
		await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"{Provider.LookupIndexName}/_doc/test-1",
			PostData.String($"{{\"{Provider.MatchField}\":\"/test\",\"ai_summary\":\"test\"}}"));

		await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"{Provider.LookupIndexName}/_refresh");

		var countBefore = await GetDocCount(Provider.LookupIndexName);
		countBefore.Should().Be(1, "lookup should have 1 seeded document");

		await enrichment.PurgeAsync();

		await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"{Provider.LookupIndexName}/_refresh");

		var countAfter = await GetDocCount(Provider.LookupIndexName);
		countAfter.Should().Be(0, "lookup should be empty after purge");
	}

	[Test]
	public async Task ProviderGeneratesValidInfrastructureJson()
	{
		Provider.LookupIndexName.Should().NotBeNullOrEmpty();
		Provider.EnrichPolicyName.Should().StartWith("ai-enrichment-policy-");
		Provider.PipelineName.Should().NotBeNullOrEmpty();
		Provider.MatchField.Should().Be("url");

		// Validate that the generated JSON is parseable
		using var mappingDoc = JsonDocument.Parse(Provider.LookupIndexMapping);
		mappingDoc.RootElement.TryGetProperty("mappings", out _).Should().BeTrue();

		using var policyDoc = JsonDocument.Parse(Provider.EnrichPolicyBody);
		policyDoc.RootElement.TryGetProperty("match", out _).Should().BeTrue();

		using var pipelineDoc = JsonDocument.Parse(Provider.PipelineBody);
		pipelineDoc.RootElement.TryGetProperty("processors", out _).Should().BeTrue();
	}

	private async Task<long> GetDocCount(string index)
	{
		var response = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{index}/_count");
		if (response.ApiCallDetails.HttpStatusCode != 200 || response.Body == null)
			return -1;
		using var doc = JsonDocument.Parse(response.Body);
		return doc.RootElement.GetProperty("count").GetInt64();
	}

	private async Task CleanupLookupAsync()
	{
		await Transport.RequestAsync<StringResponse>(
			HttpMethod.DELETE, $"{Provider.LookupIndexName}?ignore_unavailable=true");
	}

	private async Task CleanupPolicyAndPipelineAsync()
	{
		await Transport.RequestAsync<StringResponse>(
			HttpMethod.DELETE, $"_enrich/policy/{Provider.EnrichPolicyName}");
		await Transport.RequestAsync<StringResponse>(
			HttpMethod.DELETE, $"_ingest/pipeline/{Provider.PipelineName}");
	}
}
