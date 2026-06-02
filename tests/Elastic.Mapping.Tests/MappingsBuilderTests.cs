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
		// AddField on a parent, then AddProperty for the child (object parent → properties).
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("metadata", f => f.Object())
			.AddProperty("metadata.inner", f => f.Keyword());

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

	[Test]
	public void TextFieldBuilder_TermVector_AppearsInJson()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("stripped_body", f => f.Text()
				.Analyzer("standard")
				.TermVector("with_positions_offsets"));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var field = doc.RootElement.GetProperty("properties").GetProperty("stripped_body");
		field.GetProperty("type").GetString().Should().Be("text");
		field.GetProperty("term_vector").GetString().Should().Be("with_positions_offsets");
	}

	[Test]
	public void TextMultiFieldBuilder_TermVector_AppearsInJson()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.Message(f => f
				.Analyzer("standard")
				.MultiField("highlight", mf => mf.Text()
					.Analyzer("standard")
					.TermVector("with_positions_offsets")));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var highlight = doc.RootElement.GetProperty("properties").GetProperty("message")
			.GetProperty("fields").GetProperty("highlight");
		highlight.GetProperty("term_vector").GetString().Should().Be("with_positions_offsets");
	}

	[Test]
	public void SearchAsYouTypeFieldBuilder_IndexOptions_AppearsInJson()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("search_title", f => f.SearchAsYouType()
				.Analyzer("standard")
				.IndexOptions("offsets"));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var field = doc.RootElement.GetProperty("properties").GetProperty("search_title");
		field.GetProperty("type").GetString().Should().Be("search_as_you_type");
		field.GetProperty("index_options").GetString().Should().Be("offsets");
	}

	[Test]
	public void SearchAsYouTypeMultiFieldBuilder_IndexOptions_AppearsInJson()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.Message(f => f
				.Analyzer("standard")
				.MultiField("completion", mf => mf.SearchAsYouType()
					.Analyzer("standard")
					.IndexOptions("offsets")));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var completion = doc.RootElement.GetProperty("properties").GetProperty("message")
			.GetProperty("fields").GetProperty("completion");
		completion.GetProperty("index_options").GetString().Should().Be("offsets");
	}

	// ----- Explicit AddField / AddProperty intent tests -----

	[Test]
	public void AddField_TextParent_LeafUnderFields()
	{
		// AddField on a text parent → leaf goes under "fields" (multi-field)
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("summary", f => f.Text().Analyzer("standard"))
			.AddField("summary.semantic", f => f.SemanticText().InferenceId("my-elser"));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var summary = doc.RootElement.GetProperty("properties").GetProperty("summary");
		summary.GetProperty("type").GetString().Should().Be("text");
		summary.TryGetProperty("properties", out _).Should().BeFalse("leaf parent must not get a properties key");
		var semantic = summary.GetProperty("fields").GetProperty("semantic");
		semantic.GetProperty("type").GetString().Should().Be("semantic_text");
		semantic.GetProperty("inference_id").GetString().Should().Be("my-elser");
	}

	[Test]
	public void AddField_KeywordParent_LeafUnderFields()
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
		tag.TryGetProperty("properties", out _).Should().BeFalse();
		var text = tag.GetProperty("fields").GetProperty("text");
		text.GetProperty("type").GetString().Should().Be("text");
	}

	[Test]
	public void AddProperty_ObjectParent_LeafUnderProperties()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("metadata", f => f.Object())
			.AddProperty("metadata.extra", f => f.Keyword());

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var metadata = doc.RootElement.GetProperty("properties").GetProperty("metadata");
		metadata.GetProperty("type").GetString().Should().Be("object");
		metadata.TryGetProperty("fields", out _).Should().BeFalse("object parent must not get a fields key");
		var extra = metadata.GetProperty("properties").GetProperty("extra");
		extra.GetProperty("type").GetString().Should().Be("keyword");
	}

	[Test]
	public void AddField_ObjectParent_Throws()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("metadata", f => f.Object())
			.AddField("metadata.inner", f => f.Keyword());

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();

		var act = () => overrides.MergeIntoMappings(baseMappings);
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*AddProperty*");
	}

	[Test]
	public void AddProperty_LeafParent_Throws()
	{
		// message is a text field in the base mapping
		var builder = new MappingsBuilder<LogEntry>()
			.AddProperty("message.sub", f => f.Keyword());

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();

		var act = () => overrides.MergeIntoMappings(baseMappings);
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*AddField*");
	}

	[Test]
	public void AddField_MissingParent_Throws()
	{
		// "ghost" is not defined anywhere — AddField requires the parent to exist
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("ghost.child", f => f.Keyword());

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();

		var act = () => overrides.MergeIntoMappings(baseMappings);
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*parent field is not defined*");
	}

	[Test]
	public void AddProperty_MissingParent_CreatesObjectParent()
	{
		// "ghost" is not defined — AddProperty creates it as object
		var builder = new MappingsBuilder<LogEntry>()
			.AddProperty("ghost.child", f => f.Keyword());

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var ghost = doc.RootElement.GetProperty("properties").GetProperty("ghost");
		ghost.GetProperty("type").GetString().Should().Be("object");
		var child = ghost.GetProperty("properties").GetProperty("child");
		child.GetProperty("type").GetString().Should().Be("keyword");
	}

	[Test]
	public void AddField_ChildBeforeParent_LeafUnderFieldsRegardlessOfOrder()
	{
		// The ordering trap: child declared first, parent second.
		// The depth-first sort in MergeIntoMappings must make this order-independent.
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("title.semantic_text", f => f.SemanticText().InferenceId("my-elser"))
			.AddField("title", f => f.Text());

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var title = doc.RootElement.GetProperty("properties").GetProperty("title");
		title.GetProperty("type").GetString().Should().Be("text",
			"parent type must be preserved regardless of declaration order");
		title.TryGetProperty("properties", out _).Should().BeFalse(
			"leaf parent must never get a properties key");
		var semantic = title.GetProperty("fields").GetProperty("semantic_text");
		semantic.GetProperty("type").GetString().Should().Be("semantic_text");
	}

	[Test]
	public void AddProperty_ChildBeforeParent_LeafUnderPropertiesRegardlessOfOrder()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddProperty("meta.inner", f => f.Keyword())
			.AddField("meta", f => f.Object());

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var meta = doc.RootElement.GetProperty("properties").GetProperty("meta");
		meta.GetProperty("type").GetString().Should().Be("object",
			"parent type must be preserved regardless of declaration order");
		meta.TryGetProperty("fields", out _).Should().BeFalse();
		var inner = meta.GetProperty("properties").GetProperty("inner");
		inner.GetProperty("type").GetString().Should().Be("keyword");
	}

	[Test]
	public void AddField_TopLevelSingleSegment_RootProperty()
	{
		// Single-segment path: intent is ignored; leaf goes to root properties
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("standalone", f => f.Keyword());

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var standalone = doc.RootElement.GetProperty("properties").GetProperty("standalone");
		standalone.GetProperty("type").GetString().Should().Be("keyword");
	}

	[Test]
	public void AddProperty_TopLevelSingleSegment_RootProperty()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddProperty("standalone2", f => f.Keyword());

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var standalone2 = doc.RootElement.GetProperty("properties").GetProperty("standalone2");
		standalone2.GetProperty("type").GetString().Should().Be("keyword");
	}

	[Test]
	public void AddField_SamePathAddedTwice_LastWins()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("summary", f => f.Text())
			.AddField("summary.a", f => f.Keyword().IgnoreAbove(128))
			.AddField("summary.a", f => f.Keyword().IgnoreAbove(512));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var a = doc.RootElement.GetProperty("properties").GetProperty("summary")
			.GetProperty("fields").GetProperty("a");
		a.GetProperty("ignore_above").GetInt32().Should().Be(512, "last definition wins");
	}

	[Test]
	public void MappingsBuilder_Additive_SecondCallMergesMultiFields()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("title", f => f.Text().MultiField("keyword", mf => mf.Keyword()))
			.AddField("title", f => f.Text().MultiField("semantic_text", mf => mf.SemanticText()));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var title = doc.RootElement.GetProperty("properties").GetProperty("title");
		title.GetProperty("fields").GetProperty("keyword").GetProperty("type").GetString().Should().Be("keyword");
		title.GetProperty("fields").GetProperty("semantic_text").GetProperty("type").GetString().Should().Be("semantic_text");
	}

	[Test]
	public void MappingsBuilder_Additive_SecondCallCanAddSearchAnalyzerWithoutLosingMultiFields()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("title", f => f.Text()
				.MultiField("keyword", mf => mf.Keyword())
				.MultiField("completion", mf => mf.SearchAsYouType()))
			.AddField("title", f => f.Text().SearchAnalyzer("synonyms"));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var title = doc.RootElement.GetProperty("properties").GetProperty("title");
		title.GetProperty("search_analyzer").GetString().Should().Be("synonyms");
		title.GetProperty("fields").GetProperty("keyword").GetProperty("type").GetString().Should().Be("keyword");
		title.GetProperty("fields").GetProperty("completion").GetProperty("type").GetString().Should().Be("search_as_you_type");
	}

	[Test]
	public void MappingsBuilder_Clear_ResetsAccumulatedState()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("title", f => f.Text()
				.MultiField("keyword", mf => mf.Keyword())
				.MultiField("completion", mf => mf.SearchAsYouType()))
			.AddField("title", f => f.Text().Clear()
				.MultiField("semantic_text", mf => mf.SemanticText()));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var title = doc.RootElement.GetProperty("properties").GetProperty("title");
		var fields = title.GetProperty("fields");
		fields.TryGetProperty("keyword", out _).Should().BeFalse("keyword should have been cleared");
		fields.TryGetProperty("completion", out _).Should().BeFalse("completion should have been cleared");
		fields.GetProperty("semantic_text").GetProperty("type").GetString().Should().Be("semantic_text");
	}

	[Test]
	public void MappingsBuilder_Additive_SameMultiFieldNameOverwrites()
	{
		var builder = new MappingsBuilder<LogEntry>()
			.AddField("title", f => f.Text().MultiField("keyword", mf => mf.Keyword().Normalizer("first")))
			.AddField("title", f => f.Text().MultiField("keyword", mf => mf.Keyword().Normalizer("second")));

		var overrides = builder.Build();
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var keyword = doc.RootElement.GetProperty("properties").GetProperty("title").GetProperty("fields").GetProperty("keyword");
		keyword.GetProperty("normalizer").GetString().Should().Be("second");
	}
}
