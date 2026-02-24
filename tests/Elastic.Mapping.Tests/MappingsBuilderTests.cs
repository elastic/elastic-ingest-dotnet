// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Nodes;
using Elastic.Mapping.Mappings;

namespace Elastic.Mapping.Tests;

public class MappingsBuilderTests
{
	[Test]
	public void MappingsBuilder_PropertyMethod_ConfiguresField()
	{
		var builder = new MappingsBuilder<LogEntry>();

		builder.Message(f => f.Analyzer("custom_analyzer"));

		builder.HasConfiguration.Should().BeTrue();
	}

	[Test]
	public void MappingsBuilder_AddField_AppearsInMergedJson()
	{
		var builder = new MappingsBuilder<LogEntry>();
		builder.AddField("all_text", f => f.Text().Analyzer("standard"));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		merged.Should().Contain("all_text");
	}

	[Test]
	public void MappingsBuilder_AddRuntimeField_AppearsInMergedJson()
	{
		var builder = new MappingsBuilder<LogEntry>();
		builder.AddRuntimeField("day_of_week", r => r
			.Keyword()
			.Script("emit(doc['@timestamp'].value.dayOfWeekEnum.getDisplayName(TextStyle.FULL, Locale.ROOT))")
		);

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		merged.Should().Contain("runtime");
		merged.Should().Contain("day_of_week");
		merged.Should().Contain("keyword");
	}

	[Test]
	public void MappingsBuilder_AddDynamicTemplate_AppearsInMergedJson()
	{
		var builder = new MappingsBuilder<LogEntry>();
		builder.AddDynamicTemplate("strings_as_keywords", t => t
			.MatchMappingType("string")
			.Mapping(f => f.Keyword())
		);

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		merged.Should().Contain("dynamic_templates");
		merged.Should().Contain("strings_as_keywords");
	}

	[Test]
	public void MappingsBuilder_HasConfiguration_FalseWhenEmpty()
	{
		var builder = new MappingsBuilder<LogEntry>();

		builder.HasConfiguration.Should().BeFalse();
	}

	[Test]
	public void MappingsBuilder_ChainsMultipleOperations()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.Message(f => f.Analyzer("custom"))
			.AddField("extra", f => f.Keyword())
			.AddRuntimeField("computed", r => r.Long().Script("emit(1)"));

		builder.HasConfiguration.Should().BeTrue();
	}

	[Test]
	public void MappingsBuilder_Override_LastDefinitionWins()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.Message(f => f.Analyzer("first_analyzer"))
			.Message(f => f.Analyzer("second_analyzer"));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var message = doc.RootElement.GetProperty("properties").GetProperty("message");
		message.GetProperty("analyzer").GetString().Should().Be("second_analyzer");
	}

	[Test]
	public void MappingsBuilder_Override_AddFieldSamePathDoesNotThrow()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("custom_field", f => f.Text().Analyzer("first"))
			.AddField("custom_field", f => f.Text().Analyzer("second").SearchAnalyzer("search_v2"));

		var overrides = builder.Build();
		overrides.Fields.Should().ContainKey("custom_field");

		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var field = doc.RootElement.GetProperty("properties").GetProperty("custom_field");
		field.GetProperty("analyzer").GetString().Should().Be("second");
		field.GetProperty("search_analyzer").GetString().Should().Be("search_v2");
	}

	[Test]
	public void MappingsBuilder_DottedPath_ObjectParent_UsesProperties()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("metadata", f => f.Object())
			.AddField("metadata.inner", f => f.Keyword());

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var metadata = doc.RootElement.GetProperty("properties").GetProperty("metadata");
		metadata.GetProperty("type").GetString().Should().Be("object");
		var inner = metadata.GetProperty("properties").GetProperty("inner");
		inner.GetProperty("type").GetString().Should().Be("keyword");
	}

	[Test]
	public void MappingsBuilder_DottedPath_TextParent_UsesFields()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("summary", f => f.Text().Analyzer("standard"))
			.AddField("summary.raw", f => f.Keyword());

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var summary = doc.RootElement.GetProperty("properties").GetProperty("summary");
		summary.GetProperty("type").GetString().Should().Be("text");

		summary.TryGetProperty("properties", out _).Should().BeFalse(
			"leaf types should not get a 'properties' key");

		var raw = summary.GetProperty("fields").GetProperty("raw");
		raw.GetProperty("type").GetString().Should().Be("keyword");
	}

	[Test]
	public void MappingsBuilder_DottedPath_KeywordParent_UsesFields()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("tag", f => f.Keyword())
			.AddField("tag.text", f => f.Text().Analyzer("standard"));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var tag = doc.RootElement.GetProperty("properties").GetProperty("tag");
		tag.GetProperty("type").GetString().Should().Be("keyword");
		var text = tag.GetProperty("fields").GetProperty("text");
		text.GetProperty("type").GetString().Should().Be("text");
		text.GetProperty("analyzer").GetString().Should().Be("standard");
	}

	[Test]
	public void MappingsBuilder_DottedPath_MergesIntoExistingBaseField()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("message.keyword", f => f.Keyword());

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var message = doc.RootElement.GetProperty("properties").GetProperty("message");
		message.GetProperty("type").GetString().Should().Be("text",
			"the base text type should be preserved");
		var keyword = message.GetProperty("fields").GetProperty("keyword");
		keyword.GetProperty("type").GetString().Should().Be("keyword");
	}

	[Test]
	public void MappingsBuilder_TextMultiField_AppearsInFields()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.Message(f => f
				.Analyzer("custom")
				.MultiField("keyword", mf => mf.Keyword())
				.MultiField("completion", mf => mf.SearchAsYouType()));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var message = doc.RootElement.GetProperty("properties").GetProperty("message");
		var fields = message.GetProperty("fields");
		fields.GetProperty("keyword").GetProperty("type").GetString().Should().Be("keyword");
		fields.GetProperty("completion").GetProperty("type").GetString().Should().Be("search_as_you_type");
	}

	[Test]
	public void MappingsBuilder_SemanticTextMultiField_AppearsInFields()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.Message(f => f
				.Analyzer("custom")
				.MultiField("semantic", mf => mf.SemanticText().InferenceId("my-inference")));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var message = doc.RootElement.GetProperty("properties").GetProperty("message");
		var semantic = message.GetProperty("fields").GetProperty("semantic");
		semantic.GetProperty("type").GetString().Should().Be("semantic_text");
		semantic.GetProperty("inference_id").GetString().Should().Be("my-inference");
	}

	[Test]
	public void MappingsBuilder_Raw_WithSetProperties()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("custom", f => f.Raw("dense_vector")
				.Set("dims", 768)
				.Set("similarity", "cosine")
				.Set("index", true));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var custom = doc.RootElement.GetProperty("properties").GetProperty("custom");
		custom.GetProperty("type").GetString().Should().Be("dense_vector");
		custom.GetProperty("dims").GetInt32().Should().Be(768);
		custom.GetProperty("similarity").GetString().Should().Be("cosine");
		custom.GetProperty("index").GetBoolean().Should().BeTrue();
	}

	[Test]
	public void MappingsBuilder_Raw_WithPropertiesJsonObject()
	{
		var props = new JsonObject
		{
			["inference_id"] = "my-elser",
			["search_inference_id"] = "my-search-elser"
		};

		var builder = new MappingsBuilder<LogEntry>()
			.AddField("semantic_body", f => f.Raw("semantic_text").Properties(props));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var field = doc.RootElement.GetProperty("properties").GetProperty("semantic_body");
		field.GetProperty("type").GetString().Should().Be("semantic_text");
		field.GetProperty("inference_id").GetString().Should().Be("my-elser");
		field.GetProperty("search_inference_id").GetString().Should().Be("my-search-elser");
	}

	[Test]
	public void MappingsBuilder_Raw_MultiField()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.Message(f => f
				.Analyzer("custom")
				.MultiField("raw_sub", mf => mf.Raw("token_count").Set("analyzer", "standard")));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var message = doc.RootElement.GetProperty("properties").GetProperty("message");
		var rawSub = message.GetProperty("fields").GetProperty("raw_sub");
		rawSub.GetProperty("type").GetString().Should().Be("token_count");
		rawSub.GetProperty("analyzer").GetString().Should().Be("standard");
	}

	[Test]
	public void MappingsBuilder_Raw_WithMultiFields()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("body", f => f.Raw("text")
				.Set("analyzer", "standard")
				.MultiField("keyword", mf => mf.Keyword())
				.MultiField("completion", mf => mf.SearchAsYouType()));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var body = doc.RootElement.GetProperty("properties").GetProperty("body");
		body.GetProperty("type").GetString().Should().Be("text");
		body.GetProperty("analyzer").GetString().Should().Be("standard");
		var fields = body.GetProperty("fields");
		fields.GetProperty("keyword").GetProperty("type").GetString().Should().Be("keyword");
		fields.GetProperty("completion").GetProperty("type").GetString().Should().Be("search_as_you_type");
	}
}
