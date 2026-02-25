// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;

namespace Elastic.Mapping.Tests;

public class AiEnrichmentGeneratorTests
{
	private static IAiEnrichmentProvider Provider => AiTestMappingContext.AiEnrichment;

	private static readonly string[] SummaryOnly = ["ai_summary"];
	private static readonly string[] AllFields = ["ai_summary", "ai_questions"];

	[Test]
	public void GeneratesProviderInstance()
	{
		Provider.Should().NotBeNull();
		Provider.Should().BeAssignableTo<IAiEnrichmentProvider>();
	}

	[Test]
	public void EnrichmentFieldsContainsAllAiFields()
	{
		Provider.EnrichmentFields.Should().Contain("ai_summary");
		Provider.EnrichmentFields.Should().Contain("ai_questions");
		Provider.EnrichmentFields.Should().HaveCount(2);
	}

	[Test]
	public void RequiredSourceFieldsContainsAllAiInputs()
	{
		Provider.RequiredSourceFields.Should().Contain("title");
		Provider.RequiredSourceFields.Should().Contain("body");
		Provider.RequiredSourceFields.Should().HaveCount(2);
	}

	[Test]
	public void FieldPromptHashesAreNonEmptyAndStable()
	{
		Provider.FieldPromptHashes.Should().ContainKey("ai_summary");
		Provider.FieldPromptHashes.Should().ContainKey("ai_questions");

		Provider.FieldPromptHashes["ai_summary"].Should().NotBeNullOrEmpty();
		Provider.FieldPromptHashes["ai_questions"].Should().NotBeNullOrEmpty();

		Provider.FieldPromptHashes["ai_summary"].Should().NotBe(Provider.FieldPromptHashes["ai_questions"],
			"different descriptions should produce different hashes");

		// Stable across accesses
		Provider.FieldPromptHashes["ai_summary"].Should().Be(Provider.FieldPromptHashes["ai_summary"]);
	}

	[Test]
	public void FieldPromptHashFieldNamesFollowConvention()
	{
		Provider.FieldPromptHashFieldNames["ai_summary"].Should().Be("ai_summary_ph");
		Provider.FieldPromptHashFieldNames["ai_questions"].Should().Be("ai_questions_ph");
	}

	[Test]
	public void LookupIndexNameIsSet()
	{
		Provider.LookupIndexName.Should().NotBeNullOrEmpty();
	}

	[Test]
	public void MatchFieldIsUrl()
	{
		Provider.MatchField.Should().Be("url");
	}

	[Test]
	public void LookupIndexMappingContainsAllFields()
	{
		var mapping = Provider.LookupIndexMapping;
		mapping.Should().Contain("\"url\"");
		mapping.Should().Contain("\"ai_summary\"");
		mapping.Should().Contain("\"ai_questions\"");
		mapping.Should().Contain("\"ai_summary_ph\"");
		mapping.Should().Contain("\"ai_questions_ph\"");
		mapping.Should().Contain("\"created_at\"");
	}

	[Test]
	public void EnrichPolicyBodyReferencesLookupIndex()
	{
		Provider.EnrichPolicyBody.Should().Contain(Provider.LookupIndexName);
		Provider.EnrichPolicyBody.Should().Contain("\"match_field\"");
		Provider.EnrichPolicyBody.Should().Contain("\"url\"");
		Provider.EnrichPolicyBody.Should().Contain("\"ai_summary\"");
		Provider.EnrichPolicyBody.Should().Contain("\"ai_questions\"");
	}

	[Test]
	public void PipelineBodyContainsEnrichProcessor()
	{
		Provider.PipelineBody.Should().Contain("\"enrich\"");
		Provider.PipelineBody.Should().Contain(Provider.EnrichPolicyName);
		Provider.PipelineBody.Should().Contain("\"script\"");
	}

	[Test]
	public void EnrichPolicyNameIsNotEmpty()
	{
		Provider.EnrichPolicyName.Should().NotBeNullOrEmpty();
		Provider.EnrichPolicyName.Should().StartWith("ai-enrichment-policy-");
	}

	[Test]
	public void PipelineNameIsNotEmpty()
	{
		Provider.PipelineName.Should().NotBeNullOrEmpty();
	}

	[Test]
	public void BuildPromptReturnsNullForEmptyBody()
	{
		var source = JsonDocument.Parse("""{"title": "Test", "body": ""}""").RootElement;
		var result = Provider.BuildPrompt(source, Provider.EnrichmentFields);
		result.Should().BeNull("empty body input should skip enrichment");
	}

	[Test]
	public void BuildPromptReturnsPromptForValidInput()
	{
		var source = JsonDocument.Parse("""{"title": "Getting Started", "body": "This guide covers installation and setup."}""").RootElement;
		var result = Provider.BuildPrompt(source, Provider.EnrichmentFields);

		result.Should().NotBeNull();
		result.Should().Contain("Getting Started");
		result.Should().Contain("installation and setup");
		result.Should().Contain("json-schema");
		result.Should().Contain("ai_summary");
		result.Should().Contain("ai_questions");
	}

	[Test]
	public void BuildPromptIncludesRolePreamble()
	{
		var source = JsonDocument.Parse("""{"title": "Test", "body": "Content here."}""").RootElement;
		var result = Provider.BuildPrompt(source, Provider.EnrichmentFields);

		result.Should().Contain("documentation analysis assistant");
	}

	[Test]
	public void BuildPromptOnlyIncludesStaleFields()
	{
		var source = JsonDocument.Parse("""{"title": "Test", "body": "Content here."}""").RootElement;
		var result = Provider.BuildPrompt(source, SummaryOnly);

		result.Should().NotBeNull();
		result.Should().Contain("ai_summary");
		result.Should().NotContain("ai_questions", "only ai_summary is stale");
	}

	[Test]
	public void BuildPromptReturnsNullWhenNoStaleFields()
	{
		var source = JsonDocument.Parse("""{"title": "Test", "body": "Content here."}""").RootElement;
		var result = Provider.BuildPrompt(source, Array.Empty<string>());

		result.Should().BeNull("no stale fields means nothing to enrich");
	}

	[Test]
	public void ParseResponseReturnsJsonForValidLlmOutput()
	{
		var llmResponse = """{"ai_summary": "A guide to setup.", "ai_questions": ["How to install?", "How to configure?", "How to deploy?"]}""";
		var result = Provider.ParseResponse(llmResponse, Provider.EnrichmentFields);

		result.Should().NotBeNull();
		result.Should().Contain("ai_summary");
		result.Should().Contain("ai_questions");
		result.Should().Contain("ai_summary_ph");
		result.Should().Contain("ai_questions_ph");
	}

	[Test]
	public void ParseResponseStripsMarkdownFences()
	{
		var llmResponse = """
			```json
			{"ai_summary": "A guide.", "ai_questions": ["Q1?", "Q2?", "Q3?"]}
			```
			""";
		var result = Provider.ParseResponse(llmResponse, Provider.EnrichmentFields);

		result.Should().NotBeNull();
		result.Should().Contain("ai_summary");
	}

	[Test]
	public void ParseResponseReturnsNullForInvalidJson()
	{
		var result = Provider.ParseResponse("this is not json", Provider.EnrichmentFields);
		result.Should().BeNull();
	}

	[Test]
	public void ParseResponseOnlyIncludesEnrichedFields()
	{
		var llmResponse = """{"ai_summary": "Summary text."}""";
		var result = Provider.ParseResponse(llmResponse, SummaryOnly);

		result.Should().NotBeNull();
		result.Should().Contain("ai_summary");
		result.Should().Contain("ai_summary_ph");
		result.Should().NotContain("ai_questions");
	}
}
