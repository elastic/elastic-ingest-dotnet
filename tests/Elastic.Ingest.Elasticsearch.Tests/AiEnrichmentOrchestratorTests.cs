// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.Enrichment;
using Elastic.Mapping;
using Elastic.Transport;
using Elastic.Transport.VirtualizedCluster;
using Elastic.Transport.VirtualizedCluster.Components;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class AiEnrichmentOrchestratorTests
{
	private static ITransport CreateTransport() =>
		Virtual.Elasticsearch
			.Bootstrap(1)
			.ClientCalls(r => r.SucceedAlways().ReturnResponse(new { }))
			.StaticNodePool()
			.Settings(s => s.DisablePing().EnableDebugMode())
			.RequestHandler;

	private static FakeAiEnrichmentProvider CreateProvider() => new();

	[Test]
	public void ConstructorThrowsOnNullTransport()
	{
		var act = () => new AiEnrichmentOrchestrator(null!, CreateProvider());
		act.Should().Throw<ArgumentNullException>();
	}

	[Test]
	public void ConstructorThrowsOnNullProvider()
	{
		var act = () => new AiEnrichmentOrchestrator(CreateTransport(), (IAiEnrichmentProvider)null!);
		act.Should().Throw<ArgumentNullException>();
	}

	[Test]
	public void DefaultOptionsAreApplied()
	{
		var opts = new AiEnrichmentOptions();
		opts.MaxEnrichmentsPerRun.Should().Be(100);
		opts.QueryBatchSize.Should().Be(50);
		opts.InferenceEndpointId.Should().Be(".gp-llm-v2-completion");
		opts.EsqlBatchSize.Should().Be(20);
		opts.EsqlConcurrency.Should().Be(8);
		opts.CompletionTimeout.Should().Be(TimeSpan.FromMinutes(5));
		opts.CompletionMaxRetries.Should().Be(2);
		opts.MinCompletionBatchSize.Should().Be(5);
		opts.Timeout.Should().BeNull();
		opts.DrainTimeout.Should().BeNull();
	}

	[Test]
	public async Task InitializeAsyncDoesNotThrow()
	{
		using var orchestrator = new AiEnrichmentOrchestrator(CreateTransport(), CreateProvider());
		await orchestrator.InitializeAsync();
	}

	[Test]
	public async Task EnrichAsyncYieldsCompleteWithZeroCandidates()
	{
		var transport = Virtual.Elasticsearch
			.Bootstrap(1)
			.ClientCalls(r => r.SucceedAlways()
				.ReturnResponse(new { hits = new { hits = Array.Empty<object>() } }))
			.StaticNodePool()
			.Settings(s => s.DisablePing().EnableDebugMode())
			.RequestHandler;

		using var orchestrator = new AiEnrichmentOrchestrator(transport, CreateProvider());
		AiEnrichmentProgress? last = null;
		await foreach (var p in orchestrator.EnrichAsync("test-index"))
			last = p;

		last.Should().NotBeNull();
		last!.Phase.Should().Be(AiEnrichmentPhase.Complete);
		last.Enriched.Should().Be(0);
		last.Failed.Should().Be(0);
		last.TotalCandidates.Should().Be(0);
	}

	[Test]
	public void DisposeIsIdempotent()
	{
		var orchestrator = new AiEnrichmentOrchestrator(CreateTransport(), CreateProvider());
		orchestrator.Dispose();
		orchestrator.Dispose();
	}

	[Test]
	public void AiEnrichmentProgressHasExpectedProperties()
	{
		var p = new AiEnrichmentProgress
		{
			Phase = AiEnrichmentPhase.Enriching,
			Enriched = 10,
			Failed = 2,
			TotalCandidates = 50,
			Message = "test"
		};
		p.Phase.Should().Be(AiEnrichmentPhase.Enriching);
		p.Enriched.Should().Be(10);
		p.Failed.Should().Be(2);
		p.TotalCandidates.Should().Be(50);
		p.Message.Should().Be("test");
	}

	[Test]
	public async Task PurgeAsyncDoesNotThrow()
	{
		using var orchestrator = new AiEnrichmentOrchestrator(TestSetup.SharedTransport, CreateProvider());
		await orchestrator.PurgeAsync();
	}

	[Test]
	public async Task CleanupOlderThanAsyncDoesNotThrow()
	{
		using var orchestrator = new AiEnrichmentOrchestrator(TestSetup.SharedTransport, CreateProvider());
		await orchestrator.CleanupOlderThanAsync(TimeSpan.FromDays(30));
	}

	[Test]
	public void ProviderBuildPromptIncludesAllFieldsWhenNoHashes()
	{
		var provider = CreateProvider();
		var source = JsonDocument.Parse("""{"title":"Test","body":"Some content"}""").RootElement;
		var staleFields = new List<string> { "ai_summary", "ai_questions" };
		var prompt = provider.BuildPrompt(source, staleFields);
		prompt.Should().NotBeNull();
		prompt.Should().Contain("Some content");
	}

	[Test]
	public void ProviderBuildPromptReturnsNullForEmptyBody()
	{
		var provider = CreateProvider();
		var source = JsonDocument.Parse("""{"title":"Test","body":""}""").RootElement;
		var staleFields = new List<string> { "ai_summary" };
		var prompt = provider.BuildPrompt(source, staleFields);
		prompt.Should().BeNull("empty body should produce null prompt");
	}

	[Test]
	public void ProviderParseResponseReturnsNullForInvalidResponse()
	{
		var provider = CreateProvider();
		string[] fields = ["ai_summary"];
		var result = provider.ParseResponse("totally unrelated text", fields);
		result.Should().BeNull("response without ai_summary key should be null");
	}

	[Test]
	public void ExtractCompletionTextReturnsResultFromJsonResponse()
	{
		var transport = Virtual.Elasticsearch
			.Bootstrap(1)
			.ClientCalls(r => r.SucceedAlways()
				.ReturnResponse(new { completion = new[] { new { result = "Hello world" } } }))
			.StaticNodePool()
			.Settings(s => s.DisablePing().EnableDebugMode())
			.RequestHandler;

		var response = transport.Request<JsonResponse>(HttpMethod.GET, "/");
		var text = response.Get<string>("completion.0.result");

		text.Should().Be("Hello world");
	}

	[Test]
	public void ExtractCompletionTextReturnsNullForEmptyCompletion()
	{
		var transport = Virtual.Elasticsearch
			.Bootstrap(1)
			.ClientCalls(r => r.SucceedAlways()
				.ReturnResponse(new { completion = Array.Empty<object>() }))
			.StaticNodePool()
			.Settings(s => s.DisablePing().EnableDebugMode())
			.RequestHandler;

		var response = transport.Request<JsonResponse>(HttpMethod.GET, "/");
		var text = response.Get<string>("completion.0.result");

		text.Should().BeNullOrEmpty();
	}

	[Test]
	public void UrlHashIsDeterministic()
	{
		var hash1 = UrlHash("/test-page");
		var hash2 = UrlHash("/test-page");
		hash1.Should().Be(hash2);
	}

	[Test]
	public void UrlHashDiffersForDifferentUrls()
	{
		var hash1 = UrlHash("/page-a");
		var hash2 = UrlHash("/page-b");
		hash1.Should().NotBe(hash2);
	}

	/// <summary>
	/// Demonstrates the recommended pattern for time-boxed CI enrichment.
	/// Uses <c>FakeTimeProvider</c> so the test is deterministic — no wall-clock waits.
	///
	/// Real-world usage:
	/// <code>
	///   // Run enrichment for up to 20 minutes, scheduled every 30 minutes.
	///   // Timeout handles the time budget; the orchestrator derives the soft-stop
	///   // point internally and drains in-flight work before the hard deadline.
	///   // The CancellationToken is for application shutdown only.
	///   using var cts = new CancellationTokenSource();
	///   var options  = new AiEnrichmentOptions
	///   {
	///       MaxEnrichmentsPerRun = 5000,
	///       Timeout = TimeSpan.FromMinutes(20),
	///   };
	///   await foreach (var p in orchestrator.EnrichAsync("my-index", options, cts.Token))
	///       logger.LogInformation("[{Phase}] {Message}", p.Phase, p.Message);
	/// </code>
	/// </summary>
	[Test]
	public async Task GracefulStopDrainsInFlightAndBackfills()
	{
		// ── Arrange: 20 candidate documents, served by path-routed mock ──

		var candidateHits = new object[20];
		for (var i = 0; i < 20; i++)
			candidateHits[i] = new { _id = $"doc-{i}", sort = new[] { i.ToString(CultureInfo.InvariantCulture) } };

		var esqlResponse = new
		{
			columns = new[]
			{
				new { name = "_id", type = "keyword" },
				new { name = "url", type = "keyword" },
				new { name = "result", type = "keyword" }
			},
			values = new[]
			{
				new object[] { "doc-0", "/page-0", """{"ai_summary":"Summary 0"}""" },
				new object[] { "doc-1", "/page-1", """{"ai_summary":"Summary 1"}""" },
				new object[] { "doc-2", "/page-2", """{"ai_summary":"Summary 2"}""" },
				new object[] { "doc-3", "/page-3", """{"ai_summary":"Summary 3"}""" },
				new object[] { "doc-4", "/page-4", """{"ai_summary":"Summary 4"}""" },
			}
		};

		var transport = Virtual.Elasticsearch
			.Bootstrap(1)
			.ClientCalls(r => r.OnPath("_search").SucceedAlways()
				.ReturnResponse(new { hits = new { hits = candidateHits } }))
			.ClientCalls(r => r.OnPath("_query").SucceedAlways()
				.ReturnResponse(esqlResponse))
			.ClientCalls(r => r.OnPath("_bulk").SucceedAlways()
				.ReturnResponse(new { errors = false, items = new[] { new { update = new { status = 200 } } } }))
			.ClientCalls(r => r.OnPath("_update_by_query").SucceedAlways()
				.ReturnResponse(new { task = "n:1" }))
			.ClientCalls(r => r.OnPath("_tasks").SucceedAlways()
				.ReturnResponse(new { completed = true }))
			.ClientCalls(r => r.SucceedAlways().ReturnResponse(new { }))
			.StaticNodePool()
			.Settings(s => s.DisablePing().EnableDebugMode())
			.RequestHandler;

		// ── Act: advance fake clock past the soft-stop point after 2 chunks ──

		var fakeTime = new FakeTimeProvider();

		var opts = new AiEnrichmentOptions
		{
			MaxEnrichmentsPerRun = 20,
			EsqlBatchSize = 5,
			EsqlConcurrency = 1,
			Timeout = TimeSpan.FromMinutes(20),
			DrainTimeout = TimeSpan.FromMinutes(5),
			TimeProvider = fakeTime,
		};

		using var orchestrator = new AiEnrichmentOrchestrator(transport, CreateProvider());

		var phases = new List<AiEnrichmentProgress>();
		var advancedTime = false;
		await foreach (var p in orchestrator.EnrichAsync("test-index", opts))
		{
			phases.Add(p);

			// After 2 COMPLETION chunks finish, advance the fake clock past the
			// soft-stop point (15 min) but before the hard deadline (20 min).
			// This triggers graceful drainage without any wall-clock delay.
			if (!advancedTime && phases.Count(x => x.Phase == AiEnrichmentPhase.Enriching) >= 2)
			{
				fakeTime.Advance(TimeSpan.FromMinutes(16));
				advancedTime = true;
			}
		}

		// ── Assert ──

		var complete = phases.Last();
		complete.Phase.Should().Be(AiEnrichmentPhase.Complete);
		complete.Enriched.Should().Be(10, "2 of 4 chunks completed (5 docs each)");

		phases.Should().Contain(p => p.Phase == AiEnrichmentPhase.Draining);
		var draining = phases.First(p => p.Phase == AiEnrichmentPhase.Draining);
		draining.Message.Should().Contain("skipped 2 pending chunk(s) (10 docs)");

		// The full backfill pipeline still ran for the enriched subset
		phases.Should().Contain(p => p.Phase == AiEnrichmentPhase.Refreshing);
		phases.Should().Contain(p => p.Phase == AiEnrichmentPhase.ExecutingPolicy);
		phases.Should().Contain(p => p.Phase == AiEnrichmentPhase.Backfilling);

		var backfill = phases.First(p => p.Phase == AiEnrichmentPhase.Backfilling);
		backfill.Message.Should().Contain("10 enriched docs");
	}

	[Test]
	public async Task FullRunWithoutStopTokenBackfillsAllEnrichedDocs()
	{
		var candidateHits = new object[10];
		for (var i = 0; i < 10; i++)
			candidateHits[i] = new { _id = $"doc-{i}", sort = new[] { i.ToString(CultureInfo.InvariantCulture) } };

		var esqlResponse = new
		{
			columns = new[]
			{
				new { name = "_id", type = "keyword" },
				new { name = "url", type = "keyword" },
				new { name = "result", type = "keyword" }
			},
			values = new[]
			{
				new object[] { "doc-0", "/page-0", """{"ai_summary":"Summary 0"}""" },
				new object[] { "doc-1", "/page-1", """{"ai_summary":"Summary 1"}""" },
				new object[] { "doc-2", "/page-2", """{"ai_summary":"Summary 2"}""" },
				new object[] { "doc-3", "/page-3", """{"ai_summary":"Summary 3"}""" },
				new object[] { "doc-4", "/page-4", """{"ai_summary":"Summary 4"}""" },
			}
		};

		var transport = Virtual.Elasticsearch
			.Bootstrap(1)
			.ClientCalls(r => r.OnPath("_search").SucceedAlways()
				.ReturnResponse(new { hits = new { hits = candidateHits } }))
			.ClientCalls(r => r.OnPath("_query").SucceedAlways()
				.ReturnResponse(esqlResponse))
			.ClientCalls(r => r.OnPath("_bulk").SucceedAlways()
				.ReturnResponse(new { errors = false, items = new[] { new { update = new { status = 200 } } } }))
			.ClientCalls(r => r.OnPath("_update_by_query").SucceedAlways()
				.ReturnResponse(new { task = "n:1" }))
			.ClientCalls(r => r.OnPath("_tasks").SucceedAlways()
				.ReturnResponse(new { completed = true }))
			.ClientCalls(r => r.SucceedAlways().ReturnResponse(new { }))
			.StaticNodePool()
			.Settings(s => s.DisablePing().EnableDebugMode())
			.RequestHandler;

		var opts = new AiEnrichmentOptions
		{
			MaxEnrichmentsPerRun = 10,
			EsqlBatchSize = 5,
			EsqlConcurrency = 1,
		};

		using var orchestrator = new AiEnrichmentOrchestrator(transport, CreateProvider());
		var phases = new List<AiEnrichmentProgress>();
		await foreach (var p in orchestrator.EnrichAsync("test-index", opts))
			phases.Add(p);

		var complete = phases.Last();
		complete.Phase.Should().Be(AiEnrichmentPhase.Complete);
		complete.Enriched.Should().Be(10, "all 2 chunks of 5 docs completed");

		// No Draining phase when Timeout is not set and ct is not cancelled
		phases.Should().NotContain(p => p.Phase == AiEnrichmentPhase.Draining);

		// Backfill ran for all enriched docs
		phases.Should().Contain(p => p.Phase == AiEnrichmentPhase.Backfilling);
		var backfill = phases.First(p => p.Phase == AiEnrichmentPhase.Backfilling);
		backfill.Message.Should().Contain("10 enriched docs");
	}

	private static string UrlHash(string url)
	{
		var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(url));
		return Convert.ToHexString(hash).ToLowerInvariant();
	}
}

internal sealed class FakeAiEnrichmentProvider : IAiEnrichmentProvider
{
	public IReadOnlyDictionary<string, string> FieldPromptHashes { get; } =
		new Dictionary<string, string>
		{
			["ai_summary"] = "abc123",
			["ai_questions"] = "def456"
		};

	public IReadOnlyDictionary<string, string> FieldPromptHashFieldNames { get; } =
		new Dictionary<string, string>
		{
			["ai_summary"] = "ai_summary_ph",
			["ai_questions"] = "ai_questions_ph"
		};

	public string[] EnrichmentFields { get; } = ["ai_summary", "ai_questions"];
	public string[] RequiredSourceFields { get; } = ["title", "body"];

	public string? BuildPrompt(JsonElement source, IReadOnlyCollection<string> staleFields)
	{
		if (!source.TryGetProperty("body", out var body) || body.GetString() is null or "")
			return null;
		return $"Summarize: {body.GetString()}";
	}

	public string? ParseResponse(string llmResponse, IReadOnlyCollection<string> enrichedFields) =>
		llmResponse.Contains("ai_summary") ? llmResponse : null;

	public string LookupIndexName => "test-ai-enrichment-cache";
	public string LookupIndexMapping => """{"mappings":{"properties":{"url":{"type":"keyword"}}}}""";
	public string MatchField => "url";
	public string EnrichPolicyName => "test-ai-enrichment-policy";
	public string EnrichPolicyBody => """{"match":{"indices":"test-ai-enrichment-cache","match_field":"url","enrich_fields":["ai_summary"]}}""";
	public string PipelineName => "test-ai-enrichment-pipeline";
	public string PipelineBody => """{"description":"AI enrichment pipeline [fields_hash:abcd1234]","processors":[]}""";
	public string FieldsHash => "abcd1234";

	public string EsqlPromptExpression => """CONCAT(?p0, COALESCE(title, ""), ?p1, COALESCE(body, ""), ?p2)""";

	public IReadOnlyList<KeyValuePair<string, string>> EsqlPromptParams { get; } = new KeyValuePair<string, string>[]
	{
		new("p0", "Summarize: <title>"),
		new("p1", "</title><body>"),
		new("p2", "</body>"),
	};

	public AiInfrastructure CreateInfrastructure(string lookupIndexName) =>
		AiInfrastructure.FromProvider(this);
}
