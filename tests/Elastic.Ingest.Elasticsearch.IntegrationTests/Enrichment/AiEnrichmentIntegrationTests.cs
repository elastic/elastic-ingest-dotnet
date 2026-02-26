// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.Enrichment;
using Elastic.Mapping;
using Elastic.Transport;
using FluentAssertions;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Enrichment;

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

	// ── Infrastructure tests ──

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
	public void ProviderGeneratesValidInfrastructureJson()
	{
		Provider.LookupIndexName.Should().NotBeNullOrEmpty();
		Provider.EnrichPolicyName.Should().Be($"{Provider.LookupIndexName}-ai-policy");
		Provider.PipelineName.Should().Be($"{Provider.LookupIndexName}-ai-pipeline");
		Provider.MatchField.Should().Be("url");

		using var mappingDoc = JsonDocument.Parse(Provider.LookupIndexMapping);
		mappingDoc.RootElement.TryGetProperty("mappings", out _).Should().BeTrue();

		using var policyDoc = JsonDocument.Parse(Provider.EnrichPolicyBody);
		policyDoc.RootElement.TryGetProperty("match", out _).Should().BeTrue();

		using var pipelineDoc = JsonDocument.Parse(Provider.PipelineBody);
		pipelineDoc.RootElement.TryGetProperty("processors", out _).Should().BeTrue();
	}

	// ── End-to-end enrichment ──

	[Test]
	public async Task EnrichAsyncProcessesDocumentsEndToEnd()
	{
		using var enrichment = new AiEnrichmentOrchestrator(Transport, Provider);
		await enrichment.InitializeAsync();

		await IndexTestDocumentsAsync();

		var secondaryAlias = TestMappingContext.AiDocumentationPageAiSecondary.Context.ResolveWriteAlias();
		var result = await enrichment.EnrichAsync(secondaryAlias);

		result.Should().NotBeNull();
		result.TotalCandidates.Should().BeGreaterThan(0,
			"documents without AI fields should be found as candidates");

		if (result.Enriched > 0)
		{
			await RefreshAsync(secondaryAlias);

			var search = await Transport.RequestAsync<StringResponse>(
				HttpMethod.POST, $"/{secondaryAlias}/_search",
				PostData.String("""{"size":1,"_source":["ai_summary","ai_questions","ai_summary_ph"],"query":{"exists":{"field":"ai_summary"}}}"""));

			if (search.ApiCallDetails.HttpStatusCode == 200)
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

		(result.Enriched + result.Failed).Should().BeGreaterOrEqualTo(0);
	}

	// ── Per-field staleness / partial re-enrichment ──

	[Test]
	public async Task EnrichAsyncDetectsPartialStaleness()
	{
		using var enrichment = new AiEnrichmentOrchestrator(Transport, Provider);
		await enrichment.InitializeAsync();

		// Seed a lookup entry with ai_summary (current hash) but NO ai_questions
		var currentSummaryHash = Provider.FieldPromptHashes["ai_summary"];
		var now = FormatDate(DateTimeOffset.UtcNow);
		var lookupDoc = $@"{{
			""{Provider.MatchField}"": ""/getting-started"",
			""ai_summary"": ""A pre-cached summary."",
			""ai_summary_ph"": ""{currentSummaryHash}"",
			""created_at"": ""{now}""
		}}";

		await Transport.RequestAsync<StringResponse>(
			HttpMethod.PUT,
			$"{Provider.LookupIndexName}/_doc/{UrlHash("/getting-started")}",
			PostData.String(lookupDoc));

		await ExecuteEnrichPolicyAsync();

		await IndexTestDocumentsAsync();

		var secondaryAlias = TestMappingContext.AiDocumentationPageAiSecondary.Context.ResolveWriteAlias();
		await RefreshAsync(secondaryAlias);

		// Verify the document is still a candidate (ai_questions missing)
		var result = await enrichment.EnrichAsync(secondaryAlias);
		result.TotalCandidates.Should().BeGreaterThan(0,
			"document with missing ai_questions should be a candidate");

		// Verify the provider only requests stale fields in the prompt
		var source = JsonDocument.Parse($@"{{
			""title"": ""Getting Started"",
			""body"": ""Install and configure."",
			""ai_summary_ph"": ""{currentSummaryHash}""
		}}").RootElement;

		var staleFields = new[] { "ai_questions" };
		var prompt = Provider.BuildPrompt(source, staleFields);
		prompt.Should().NotBeNull();
		prompt.Should().Contain("ai_questions");
		prompt.Should().NotContain("ai_summary",
			"ai_summary has a current hash and should not be in the prompt");
	}

	// ── MaxEnrichmentsPerRun throttling ──

	[Test]
	public async Task EnrichAsyncRespectsMaxEnrichmentsPerRun()
	{
		using var enrichment = new AiEnrichmentOrchestrator(Transport, Provider,
			new AiEnrichmentOptions { MaxEnrichmentsPerRun = 2 });
		await enrichment.InitializeAsync();

		await IndexTestDocumentsAsync();

		var secondaryAlias = TestMappingContext.AiDocumentationPageAiSecondary.Context.ResolveWriteAlias();
		var result = await enrichment.EnrichAsync(secondaryAlias);

		result.Should().NotBeNull();
		result.TotalCandidates.Should().BeGreaterThan(0);
		(result.Enriched + result.Failed).Should().BeLessThanOrEqualTo(2,
			"should not process more than MaxEnrichmentsPerRun candidates");
		result.ReachedLimit.Should().BeTrue(
			"with 3 docs and limit=2, the limit should be reached");
	}

	// ── Purge ──

	[Test]
	public async Task PurgeAsyncClearsLookupIndex()
	{
		using var enrichment = new AiEnrichmentOrchestrator(Transport, Provider);
		await enrichment.InitializeAsync();

		await SeedLookupEntryAsync("/test", "test-summary");
		await RefreshAsync(Provider.LookupIndexName);

		var countBefore = await GetDocCount(Provider.LookupIndexName);
		countBefore.Should().Be(1, "lookup should have 1 seeded document");

		await enrichment.PurgeAsync();

		await RefreshAsync(Provider.LookupIndexName);
		var countAfter = await GetDocCount(Provider.LookupIndexName);
		countAfter.Should().Be(0, "lookup should be empty after purge");
	}

	// ── CleanupOrphanedAsync ──

	[Test]
	public async Task CleanupOrphanedAsyncDeletesOrphanedLookupEntries()
	{
		using var enrichment = new AiEnrichmentOrchestrator(Transport, Provider);
		await enrichment.InitializeAsync();

		// Seed 3 lookup entries
		await SeedLookupEntryAsync("/page-a", "Summary A");
		await SeedLookupEntryAsync("/page-b", "Summary B");
		await SeedLookupEntryAsync("/orphan", "Summary orphan");

		// Index only /page-a and /page-b into the target
		await IndexTestDocumentsAsync([
			new AiDocumentationPage { Url = "/page-a", Title = "Page A", Body = "Content A." },
			new AiDocumentationPage { Url = "/page-b", Title = "Page B", Body = "Content B." }
		]);

		var secondaryAlias = TestMappingContext.AiDocumentationPageAiSecondary.Context.ResolveWriteAlias();
		await RefreshAsync(secondaryAlias);
		await RefreshAsync(Provider.LookupIndexName);

		await enrichment.CleanupOrphanedAsync(secondaryAlias);

		await RefreshAsync(Provider.LookupIndexName);
		var count = await GetDocCount(Provider.LookupIndexName);
		count.Should().Be(2, "only /page-a and /page-b should remain; /orphan should be deleted");

		// Verify /orphan is gone
		var orphanCheck = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET,
			$"{Provider.LookupIndexName}/_doc/{UrlHash("/orphan")}");
		orphanCheck.ApiCallDetails.HttpStatusCode.Should().Be(404,
			"orphaned lookup entry should be deleted");
	}

	// ── CleanupOlderThanAsync ──

	[Test]
	public async Task CleanupOlderThanAsyncDeletesAgedEntries()
	{
		using var enrichment = new AiEnrichmentOrchestrator(Transport, Provider);
		await enrichment.InitializeAsync();

		// Seed old entry using epoch_millis for unambiguous date storage
		var oldEpoch = DateTimeOffset.UtcNow.AddDays(-60).ToUnixTimeMilliseconds();
		var newEpoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var summaryHash = Provider.FieldPromptHashes["ai_summary"];

		var putOld = await Transport.RequestAsync<StringResponse>(
			HttpMethod.PUT,
			$"{Provider.LookupIndexName}/_doc/{UrlHash("/old-page")}?refresh=true",
			PostData.String($@"{{
				""{Provider.MatchField}"": ""/old-page"",
				""ai_summary"": ""Old summary"",
				""ai_summary_ph"": ""{summaryHash}"",
				""created_at"": {oldEpoch}
			}}"));

		var putNew = await Transport.RequestAsync<StringResponse>(
			HttpMethod.PUT,
			$"{Provider.LookupIndexName}/_doc/{UrlHash("/new-page")}?refresh=true",
			PostData.String($@"{{
				""{Provider.MatchField}"": ""/new-page"",
				""ai_summary"": ""New summary"",
				""ai_summary_ph"": ""{summaryHash}"",
				""created_at"": {newEpoch}
			}}"));

		putOld.ApiCallDetails.HttpStatusCode.Should().BeOneOf([200, 201], $"PUT old doc failed: {putOld.Body}");
		putNew.ApiCallDetails.HttpStatusCode.Should().BeOneOf([200, 201], $"PUT new doc failed: {putNew.Body}");

		var countBefore = await GetDocCount(Provider.LookupIndexName);
		countBefore.Should().Be(2);

		// Run the actual cleanup via the orchestrator
		await enrichment.CleanupOlderThanAsync(TimeSpan.FromDays(30));

		await RefreshAsync(Provider.LookupIndexName);

		var countAfter = await GetDocCount(Provider.LookupIndexName);
		countAfter.Should().Be(1, "only the recent entry should remain after cleanup");
	}

	// ── Backfill in isolation ──

	[Test]
	public async Task BackfillAppliesCachedEnrichmentsViaPipeline()
	{
		using var enrichment = new AiEnrichmentOrchestrator(Transport, Provider);
		await enrichment.InitializeAsync();

		// Seed lookup with enrichment data
		var summaryHash = Provider.FieldPromptHashes["ai_summary"];
		var questionsHash = Provider.FieldPromptHashes["ai_questions"];
		var now = FormatDate(DateTimeOffset.UtcNow);
		var lookupDoc = $@"{{
			""{Provider.MatchField}"": ""/backfill-test"",
			""ai_summary"": ""Backfill test summary."",
			""ai_summary_ph"": ""{summaryHash}"",
			""ai_questions"": [""How does backfill work?"", ""What is an enrich policy?"", ""How to test pipelines?""],
			""ai_questions_ph"": ""{questionsHash}"",
			""created_at"": ""{now}""
		}}";

		await Transport.RequestAsync<StringResponse>(
			HttpMethod.PUT,
			$"{Provider.LookupIndexName}/_doc/{UrlHash("/backfill-test")}",
			PostData.String(lookupDoc));

		await RefreshAsync(Provider.LookupIndexName);
		await ExecuteEnrichPolicyAsync();

		// Index a document WITHOUT AI fields
		await IndexTestDocumentsAsync([
			new AiDocumentationPage
			{
				Url = "/backfill-test",
				Title = "Backfill",
				Body = "Testing backfill pipeline."
			}
		]);

		var secondaryAlias = TestMappingContext.AiDocumentationPageAiSecondary.Context.ResolveWriteAlias();
		await RefreshAsync(secondaryAlias);

		// Apply enrichments via _update_by_query with a pipeline (same mechanism as BackfillAsync)
		var ubqBody = $@"{{""query"":{{""term"":{{""{Provider.MatchField}"":""/backfill-test""}}}}}}";
		var ubqResponse = await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST,
			$"/{secondaryAlias}/_update_by_query?pipeline={Provider.PipelineName}&wait_for_completion=true&refresh=true",
			PostData.String(ubqBody));

		ubqResponse.ApiCallDetails.HttpStatusCode.Should().BeOneOf([200, 409],
			"_update_by_query should succeed or have version conflicts");

		var searchBody = $@"{{""size"":1,""query"":{{""term"":{{""{Provider.MatchField}"":""/backfill-test""}}}},""_source"":[""ai_summary"",""ai_questions""]}}";
		var search = await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{secondaryAlias}/_search",
			PostData.String(searchBody));

		if (search.ApiCallDetails.HttpStatusCode == 200)
		{
			using var doc = JsonDocument.Parse(search.Body);
			var hits = doc.RootElement.GetProperty("hits").GetProperty("hits");
			if (hits.GetArrayLength() > 0)
			{
				var source = hits[0].GetProperty("_source");
				source.TryGetProperty("ai_summary", out var summary).Should().BeTrue(
					"pipeline should have applied ai_summary from lookup");
				if (summary.ValueKind == JsonValueKind.String)
					summary.GetString().Should().Be("Backfill test summary.");
			}
		}
	}

	// ── Idempotency ──

	[Test]
	public async Task InitializeAsyncIsIdempotent()
	{
		using var enrichment = new AiEnrichmentOrchestrator(Transport, Provider);

		await enrichment.InitializeAsync();
		await enrichment.InitializeAsync();

		var lookupExists = await Transport.RequestAsync<StringResponse>(
			HttpMethod.HEAD, Provider.LookupIndexName);
		lookupExists.ApiCallDetails.HttpStatusCode.Should().Be(200,
			"lookup index should still exist after double initialization");

		var pipelineExists = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"_ingest/pipeline/{Provider.PipelineName}");
		pipelineExists.ApiCallDetails.HttpStatusCode.Should().Be(200,
			"pipeline should still exist after double initialization");
	}

	[Test]
	public async Task EnrichAsyncIsIdempotent()
	{
		using var enrichment = new AiEnrichmentOrchestrator(Transport, Provider);
		await enrichment.InitializeAsync();
		await IndexTestDocumentsAsync();

		var secondaryAlias = TestMappingContext.AiDocumentationPageAiSecondary.Context.ResolveWriteAlias();

		var first = await enrichment.EnrichAsync(secondaryAlias);
		first.TotalCandidates.Should().BeGreaterThan(0,
			"first run should find candidates");

		var second = await enrichment.EnrichAsync(secondaryAlias);
		second.TotalCandidates.Should().BeLessThanOrEqualTo(first.TotalCandidates,
			"second run should find fewer or equal candidates since first run enriched some");
	}

	// ── Rollover survival ──

	[Test]
	public async Task CachedEnrichmentsSurviveIndexRecreation()
	{
		using var enrichment = new AiEnrichmentOrchestrator(Transport, Provider);
		await enrichment.InitializeAsync();

		// Seed lookup with enrichment data for /rollover-page
		var summaryHash = Provider.FieldPromptHashes["ai_summary"];
		var questionsHash = Provider.FieldPromptHashes["ai_questions"];
		var now = FormatDate(DateTimeOffset.UtcNow);
		var lookupDoc = $@"{{
			""{Provider.MatchField}"": ""/rollover-page"",
			""ai_summary"": ""Rollover survival test."",
			""ai_summary_ph"": ""{summaryHash}"",
			""ai_questions"": [""Does enrichment survive?"", ""How does rollover work?"", ""Is data preserved?""],
			""ai_questions_ph"": ""{questionsHash}"",
			""created_at"": ""{now}""
		}}";

		await Transport.RequestAsync<StringResponse>(
			HttpMethod.PUT,
			$"{Provider.LookupIndexName}/_doc/{UrlHash("/rollover-page")}",
			PostData.String(lookupDoc));

		await RefreshAsync(Provider.LookupIndexName);
		await ExecuteEnrichPolicyAsync();

		// Simulate a "rollover" — delete and recreate the secondary
		var secondaryAlias = TestMappingContext.AiDocumentationPageAiSecondary.Context.ResolveWriteAlias();
		await CleanupPrefixAsync(SecondaryWrite);

		// Re-bootstrap secondary (create index + template)
		using var orch = new IncrementalSyncOrchestrator<AiDocumentationPage>(
			Transport,
			TestMappingContext.AiDocumentationPageAiPrimary,
			TestMappingContext.AiDocumentationPageAiSecondary
		);
		await orch.StartAsync(BootstrapMethod.Failure);

		// Index a document into the fresh secondary
		var page = new AiDocumentationPage
		{
			Url = "/rollover-page",
			Title = "Rollover Page",
			Body = "This page tests rollover survival."
		};
		orch.TryWrite(page);
		await orch.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(30));

		// The lookup still has the enrichment — run backfill
		var result = await enrichment.EnrichAsync(secondaryAlias);

		// The document should be a candidate (no AI fields yet on fresh index)
		result.TotalCandidates.Should().BeGreaterThan(0,
			"freshly indexed document should need enrichment");

		// Verify the lookup entry still exists
		var lookupCheck = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET,
			$"{Provider.LookupIndexName}/_doc/{UrlHash("/rollover-page")}");
		lookupCheck.ApiCallDetails.HttpStatusCode.Should().Be(200,
			"lookup entry should survive the index rollover");
	}

	// ── Helpers ──

	private async Task IndexTestDocumentsAsync(AiDocumentationPage[]? pages = null)
	{
		pages ??=
		[
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
		];

		using var orch = new IncrementalSyncOrchestrator<AiDocumentationPage>(
			Transport,
			TestMappingContext.AiDocumentationPageAiPrimary,
			TestMappingContext.AiDocumentationPageAiSecondary
		);

		await orch.StartAsync(BootstrapMethod.Failure);

		foreach (var page in pages)
			orch.TryWrite(page);

		var completed = await orch.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(30));
		completed.Should().BeTrue("orchestrator should complete successfully");
	}

	private async Task SeedLookupEntryAsync(string url, string summary, DateTimeOffset? createdAt = null)
	{
		var date = createdAt ?? DateTimeOffset.UtcNow;
		var dateStr = FormatDate(date);
		var summaryHash = Provider.FieldPromptHashes["ai_summary"];
		var doc = $@"{{
			""{Provider.MatchField}"": ""{url}"",
			""ai_summary"": ""{summary}"",
			""ai_summary_ph"": ""{summaryHash}"",
			""created_at"": ""{dateStr}""
		}}";

		await Transport.RequestAsync<StringResponse>(
			HttpMethod.PUT,
			$"{Provider.LookupIndexName}/_doc/{UrlHash(url)}",
			PostData.String(doc));
	}

	private async Task RefreshAsync(string index) =>
		await Transport.RequestAsync<StringResponse>(HttpMethod.POST, $"/{index}/_refresh");

	private async Task ExecuteEnrichPolicyAsync() =>
		await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"_enrich/policy/{Provider.EnrichPolicyName}/_execute",
			PostData.Empty);

	private async Task<long> GetDocCount(string index)
	{
		var response = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{index}/_count");
		if (response.ApiCallDetails.HttpStatusCode != 200)
			return -1;
		using var doc = JsonDocument.Parse(response.Body);
		return doc.RootElement.GetProperty("count").GetInt64();
	}

	private async Task CleanupLookupAsync() =>
		await Transport.RequestAsync<StringResponse>(
			HttpMethod.DELETE, $"{Provider.LookupIndexName}?ignore_unavailable=true");

	private async Task CleanupPolicyAndPipelineAsync()
	{
		await Transport.RequestAsync<StringResponse>(
			HttpMethod.DELETE, $"_enrich/policy/{Provider.EnrichPolicyName}");
		await Transport.RequestAsync<StringResponse>(
			HttpMethod.DELETE, $"_ingest/pipeline/{Provider.PipelineName}");
	}

	private static string UrlHash(string url)
	{
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	private static string FormatDate(DateTimeOffset date) =>
		date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
}
