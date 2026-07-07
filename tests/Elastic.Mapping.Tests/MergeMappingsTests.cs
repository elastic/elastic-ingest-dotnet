// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Mapping.Analysis;
using Elastic.Mapping.Mappings;

namespace Elastic.Mapping.Tests;

public class MergeMappingsTests
{
	[Test]
	public void MappingsBuilder_Merge_NoOverlap_AddsMissingPaths()
	{
		var builder = new MappingsBuilder<MergeBaseDocument>()
			.Merge(MergeTestMappingContext.MergeSourceDocument);

		var overrides = builder.Build();
		var baseMappings = MergeTestMappingContext.MergeBaseDocument.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var properties = doc.RootElement.GetProperty("properties");
		properties.GetProperty("extraField").GetProperty("type").GetString().Should().Be("keyword");
	}

	[Test]
	public void MappingsBuilder_Merge_FullOverlap_IsNoOp()
	{
		var builder = new MappingsBuilder<MergeBaseDocument>()
			.Merge(MergeTestMappingContext.MergeSourceDocument);

		var overrides = builder.Build();
		var baseMappings = MergeTestMappingContext.MergeBaseDocument.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var value = doc.RootElement.GetProperty("properties").GetProperty("value");
		value.GetProperty("type").GetString().Should().Be("long");
	}

	[Test]
	public void MappingsBuilder_Merge_GenuineConflict_TargetWins()
	{
		var builder = new MappingsBuilder<MergeBaseDocument>()
			.Merge(MergeTestMappingContext.MergeSourceDocument);

		var overrides = builder.Build();
		var baseMappings = MergeTestMappingContext.MergeBaseDocument.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var conflictField = doc.RootElement.GetProperty("properties").GetProperty("conflictField");
		conflictField.GetProperty("type").GetString().Should().Be("keyword",
			"MergeBaseDocument's own type must win over MergeSourceDocument's conflicting type");
		conflictField.TryGetProperty("analyzer", out _).Should().BeFalse(
			"the source's text-specific config must not leak into the target's keyword definition");
	}

	[Test]
	public void MappingsBuilder_Merge_GenuineConflict_ExplicitOverrideAlsoWins()
	{
		// The target explicitly declares "conflictField" itself (not just via its generated
		// baseline) before merging — the explicit call must win the same way the baseline does.
		var builder = new MappingsBuilder<MergeBaseDocument>()
			.AddField("conflictField", f => f.Keyword().IgnoreAbove(64))
			.Merge(MergeTestMappingContext.MergeSourceDocument);

		var overrides = builder.Build();
		var baseMappings = MergeTestMappingContext.MergeBaseDocument.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var conflictField = doc.RootElement.GetProperty("properties").GetProperty("conflictField");
		conflictField.GetProperty("type").GetString().Should().Be("keyword");
		conflictField.GetProperty("ignore_above").GetInt32().Should().Be(64);
	}

	[Test]
	public void MappingsBuilder_Merge_ConfigureOverload_CapturesManualOverrides()
	{
		var builder = new MappingsBuilder<MergeBaseDocument>()
			.Merge(MergeTestMappingContext.MergeSourceDocument, b => b
				.AddField("extraConfigured", f => f.Text().Analyzer("standard")));

		var overrides = builder.Build();
		var baseMappings = MergeTestMappingContext.MergeBaseDocument.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var properties = doc.RootElement.GetProperty("properties");

		// The manual AddField from the configure lambda is present...
		var extraConfigured = properties.GetProperty("extraConfigured");
		extraConfigured.GetProperty("type").GetString().Should().Be("text");
		extraConfigured.GetProperty("analyzer").GetString().Should().Be("standard");

		// ...alongside MergeSourceDocument's own generated defaults.
		properties.GetProperty("extraField").GetProperty("type").GetString().Should().Be("keyword");
	}

	[Test]
	public void MappingsBuilder_Merge_HasConfiguration_TrueAfterMerge()
	{
		var builder = new MappingsBuilder<MergeBaseDocument>()
			.Merge(MergeTestMappingContext.MergeSourceDocument);

		builder.HasConfiguration.Should().BeTrue();
	}

	[Test]
	public void AnalysisBuilder_Merge_NoOverlap_AddsMissingAnalyzer()
	{
		var builder = new AnalysisBuilder()
			.Merge(MergeTestMappingContext.MergeSourceDocument);

		var settings = builder.Build();
		settings.Analyzers.Should().ContainKey("source_only_analyzer");
	}

	[Test]
	public void AnalysisBuilder_Merge_NameConflict_TargetWins()
	{
		var builder = new AnalysisBuilder()
			.Analyzer("shared_analyzer", a => a
				.Custom()
				.Tokenizer(BuiltInAnalysis.Tokenizers.Standard)
				.Filters(BuiltInAnalysis.TokenFilters.Lowercase))
			.Merge(MergeTestMappingContext.MergeSourceDocument);

		var settings = builder.Build();
		var json = settings.ToJson().ToJsonString();
		json.Should().Contain("standard").And.NotContain("whitespace");
	}

	[Test]
	public void AnalysisBuilder_Merge_DoesNotThrowOnDuplicateName()
	{
		var act = () => new AnalysisBuilder()
			.Analyzer("shared_analyzer", a => a.Custom().Tokenizer(BuiltInAnalysis.Tokenizers.Standard))
			.Merge(MergeTestMappingContext.MergeSourceDocument)
			.Build();

		act.Should().NotThrow();
	}

	// =========================================================================
	// Templated (NameTemplate-based) merge tests
	// =========================================================================

	[Test]
	public void TemplatedResolver_Implements_IStaticMappingResolver()
	{
		var resolver = TemplatedMergeTestMappingContext.TemplatedMergeSourceDocument;
		(resolver is IStaticMappingResolver<TemplatedMergeSourceDocument>).Should().BeTrue();
		resolver.Context.Should().NotBeNull();
		resolver.Context.GetMappingsJson().Should().NotBeNullOrEmpty();
	}

	[Test]
	public void MappingsBuilder_Merge_TemplatedResolver_ViaInterface_AddsMissingPaths()
	{
		var builder = new MappingsBuilder<TemplatedMergeBaseDocument>()
			.Merge(TemplatedMergeTestMappingContext.TemplatedMergeSourceDocument);

		var overrides = builder.Build();
		var baseMappings = TemplatedMergeTestMappingContext.TemplatedMergeBaseDocument.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var properties = doc.RootElement.GetProperty("properties");
		properties.GetProperty("extraField").GetProperty("type").GetString().Should().Be("keyword");
	}

	[Test]
	public void MappingsBuilder_Merge_TemplatedResolver_ViaInterface_TargetWins()
	{
		var builder = new MappingsBuilder<TemplatedMergeBaseDocument>()
			.Merge(TemplatedMergeTestMappingContext.TemplatedMergeSourceDocument);

		var overrides = builder.Build();
		var baseMappings = TemplatedMergeTestMappingContext.TemplatedMergeBaseDocument.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var conflictField = doc.RootElement.GetProperty("properties").GetProperty("conflictField");
		conflictField.GetProperty("type").GetString().Should().Be("keyword");
	}

	[Test]
	public void MappingsBuilder_Merge_TemplatedResolver_ViaContext_AddsMissingPaths()
	{
		var context = TemplatedMergeTestMappingContext.TemplatedMergeSourceDocument
			.CreateContext("articles", "production");

		var builder = new MappingsBuilder<TemplatedMergeBaseDocument>()
			.Merge(context);

		var overrides = builder.Build();
		var baseMappings = TemplatedMergeTestMappingContext.TemplatedMergeBaseDocument.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var properties = doc.RootElement.GetProperty("properties");
		properties.GetProperty("extraField").GetProperty("type").GetString().Should().Be("keyword");
	}

	[Test]
	public void MappingsBuilder_Merge_TemplatedResolver_ViaContext_WithConfigure()
	{
		var context = TemplatedMergeTestMappingContext.TemplatedMergeSourceDocument
			.CreateContext("articles", "production");

		var builder = new MappingsBuilder<TemplatedMergeBaseDocument>()
			.Merge<TemplatedMergeSourceDocument>(context, b => b
				.AddField("extraConfigured", f => f.Text().Analyzer("standard")));

		var overrides = builder.Build();
		var baseMappings = TemplatedMergeTestMappingContext.TemplatedMergeBaseDocument.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		using var doc = JsonDocument.Parse(merged);
		var properties = doc.RootElement.GetProperty("properties");
		properties.GetProperty("extraConfigured").GetProperty("type").GetString().Should().Be("text");
		properties.GetProperty("extraField").GetProperty("type").GetString().Should().Be("keyword");
	}

	[Test]
	public void AnalysisBuilder_Merge_TemplatedResolver_ViaInterface()
	{
		var builder = new AnalysisBuilder()
			.Merge(TemplatedMergeTestMappingContext.TemplatedMergeSourceDocument);

		var settings = builder.Build();
		settings.Analyzers.Should().ContainKey("templated_source_analyzer");
	}

	[Test]
	public void AnalysisBuilder_Merge_TemplatedResolver_ViaContext()
	{
		var context = TemplatedMergeTestMappingContext.TemplatedMergeSourceDocument
			.CreateContext("articles", "production");

		var builder = new AnalysisBuilder()
			.Merge(context);

		var settings = builder.Build();
		settings.Analyzers.Should().ContainKey("templated_source_analyzer");
	}
}
