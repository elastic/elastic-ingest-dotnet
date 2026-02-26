// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.Enrichment;
using Elastic.Mapping;
using Elastic.Transport;
using Elastic.Transport.VirtualizedCluster;
using Elastic.Transport.VirtualizedCluster.Components;
using FluentAssertions;
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
		var act = () => new AiEnrichmentOrchestrator(CreateTransport(), null!);
		act.Should().Throw<ArgumentNullException>();
	}

	[Test]
	public void DefaultOptionsAreApplied()
	{
		var opts = new AiEnrichmentOptions();
		opts.MaxEnrichmentsPerRun.Should().Be(100);
		opts.MaxConcurrency.Should().Be(4);
		opts.QueryBatchSize.Should().Be(50);
		opts.InferenceEndpointId.Should().Be(".gp-llm-v2-completion");
	}

	[Test]
	public async Task InitializeAsyncDoesNotThrow()
	{
		using var orchestrator = new AiEnrichmentOrchestrator(CreateTransport(), CreateProvider());
		await orchestrator.InitializeAsync();
	}

	[Test]
	public async Task EnrichAsyncReturnsEmptyResultWhenNoCandidates()
	{
		var transport = Virtual.Elasticsearch
			.Bootstrap(1)
			.ClientCalls(r => r.SucceedAlways()
				.ReturnResponse(new { hits = new { hits = Array.Empty<object>() } }))
			.StaticNodePool()
			.Settings(s => s.DisablePing().EnableDebugMode())
			.RequestHandler;

		using var orchestrator = new AiEnrichmentOrchestrator(transport, CreateProvider());
		var result = await orchestrator.EnrichAsync("test-index");

		result.Should().NotBeNull();
		result.Enriched.Should().Be(0);
		result.Failed.Should().Be(0);
		result.TotalCandidates.Should().Be(0);
		result.ReachedLimit.Should().BeFalse();
	}

	[Test]
	public void DisposeIsIdempotent()
	{
		var orchestrator = new AiEnrichmentOrchestrator(CreateTransport(), CreateProvider());
		orchestrator.Dispose();
		orchestrator.Dispose();
	}

	[Test]
	public void AiEnrichmentResultInitializesWithDefaults()
	{
		var result = new AiEnrichmentResult();
		result.TotalCandidates.Should().Be(0);
		result.Enriched.Should().Be(0);
		result.Failed.Should().Be(0);
		result.ReachedLimit.Should().BeFalse();
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
}
