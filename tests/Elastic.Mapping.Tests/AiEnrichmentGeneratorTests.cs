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
		Provider.EnrichPolicyName.Should().StartWith($"{Provider.LookupIndexName}-policy-");
	}

	[Test]
	public void PipelineNameIsNotEmpty()
	{
		Provider.PipelineName.Should().NotBeNullOrEmpty();
		Provider.PipelineName.Should().StartWith($"{Provider.LookupIndexName}-pipeline-");
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

	// ── ParseResponse edge cases ──

	[Test]
	public void ParseResponseReturnsNullForEmptyObject()
	{
		var result = Provider.ParseResponse("{}", AllFields);
		result.Should().BeNull("empty LLM response should produce null");
	}

	[Test]
	public void ParseResponseIgnoresExtraFields()
	{
		var llmResponse = """{"ai_summary": "Summary.", "unknown_field": "ignored", "ai_questions": ["Q1?","Q2?","Q3?"]}""";
		var result = Provider.ParseResponse(llmResponse, AllFields);

		result.Should().NotBeNull();
		result.Should().Contain("ai_summary");
		result.Should().Contain("ai_questions");
		result.Should().NotContain("unknown_field");
	}

	[Test]
	public void ParseResponseHandlesMissingRequestedField()
	{
		var llmResponse = """{"ai_summary": "Only summary, no questions."}""";
		var result = Provider.ParseResponse(llmResponse, AllFields);

		result.Should().NotBeNull();
		result.Should().Contain("ai_summary");
		result.Should().NotContain("ai_questions",
			"field not present in LLM response should not appear");
	}

	[Test]
	public void ParseResponseHandlesUnicodeContent()
	{
		var llmResponse = """{"ai_summary": "Résumé avec des caractères spéciaux: 日本語 中文 한국어"}""";
		var result = Provider.ParseResponse(llmResponse, SummaryOnly);

		result.Should().NotBeNull();
		result.Should().Contain("ai_summary");

		using var doc = JsonDocument.Parse(result!);
		var summary = doc.RootElement.GetProperty("ai_summary").GetString();
		summary.Should().Contain("Résumé");
		summary.Should().Contain("日本語");
	}

	[Test]
	public void ParseResponseReturnsNullForJsonArray()
	{
		var result = Provider.ParseResponse("[1,2,3]", AllFields);
		result.Should().BeNull("array root is not a valid response");
	}

	[Test]
	public void ParseResponseReturnsNullForScalarJson()
	{
		var result = Provider.ParseResponse("\"just a string\"", AllFields);
		result.Should().BeNull("scalar root is not a valid response");
	}

	[Test]
	public void ParseResponseHandlesNestedMarkdownFences()
	{
		var llmResponse = """
			```json
			{"ai_summary": "Use ```code blocks``` in markdown.", "ai_questions": ["How to format?", "What is markdown?", "How to nest?"]}
			```
			""";
		var result = Provider.ParseResponse(llmResponse, AllFields);

		result.Should().NotBeNull();
		result.Should().Contain("ai_summary");
	}

	[Test]
	public void BuildPromptReturnsNullWhenAnyInputIsEmpty()
	{
		var sourceNoTitle = JsonDocument.Parse("""{"title": "", "body": "Some content."}""").RootElement;
		Provider.BuildPrompt(sourceNoTitle, AllFields).Should().BeNull(
			"empty title should cause prompt to be skipped");

		var sourceNoBody = JsonDocument.Parse("""{"title": "Title", "body": ""}""").RootElement;
		Provider.BuildPrompt(sourceNoBody, AllFields).Should().BeNull(
			"empty body should cause prompt to be skipped");
	}

	[Test]
	public void BuildPromptReturnsNullWhenInputIsMissing()
	{
		var source = JsonDocument.Parse("""{"title": "Title"}""").RootElement;
		Provider.BuildPrompt(source, AllFields).Should().BeNull(
			"missing body field should cause prompt to be skipped");
	}
}
