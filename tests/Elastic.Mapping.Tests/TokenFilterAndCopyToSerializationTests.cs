// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Analysis;
using Elastic.Mapping.Analysis.Definitions;
using Elastic.Mapping.Mappings.Builders;
using Elastic.Mapping.Mappings.Definitions;

namespace Elastic.Mapping.Tests;

/// <summary>
/// Serialization coverage for the <c>stemmer_override</c> token filter and keyword-field
/// <c>copy_to</c> support — both added to unblock relevance work in the website-search-data
/// consumer repo (curated morphology overrides + content_type/navigation_section routing
/// into a shared content_tags field).
/// </summary>
public class TokenFilterAndCopyToSerializationTests
{
	[Test]
	public void StemmerOverrideFilterDefinition_SerializesRules()
	{
		var definition = new StemmerOverrideFilterDefinition(Rules: ["configuration => config", "installation => install"]);
		var json = definition.ToJson();

		json["type"]!.GetValue<string>().Should().Be("stemmer_override");
		json["rules"]!.AsArray().Select(n => n!.GetValue<string>())
			.Should().BeEquivalentTo(["configuration => config", "installation => install"]);
		json["rules_path"].Should().BeNull();
	}

	[Test]
	public void StemmerOverrideFilterDefinition_SerializesRulesPath()
	{
		var definition = new StemmerOverrideFilterDefinition(RulesPath: "analysis/stemmer_override.txt");
		var json = definition.ToJson();

		json["rules_path"]!.GetValue<string>().Should().Be("analysis/stemmer_override.txt");
		json["rules"].Should().BeNull();
	}

	[Test]
	public void TokenFilterBuilder_StemmerOverride_BuildsExpectedDefinition()
	{
		var analysis = new AnalysisBuilder()
			.TokenFilter("morphology_override", tf => tf.StemmerOverride().Rules("auth => authentication"))
			.Build();

		var definition = (StemmerOverrideFilterDefinition)analysis.TokenFilters["morphology_override"];
		definition.Rules.Should().ContainSingle().Which.Should().Be("auth => authentication");
	}

	[Test]
	public void KeywordFieldDefinition_SerializesCopyTo()
	{
		var definition = new KeywordFieldDefinition(CopyTo: "content_tags");
		var json = definition.ToJson();

		json["copy_to"]!.GetValue<string>().Should().Be("content_tags");
	}

	[Test]
	public void KeywordFieldDefinition_OmitsCopyTo_WhenNotSet()
	{
		var definition = new KeywordFieldDefinition();
		var json = definition.ToJson();

		json["copy_to"].Should().BeNull();
	}

	[Test]
	public void FieldBuilder_Keyword_CopyTo_BuildsExpectedDefinition()
	{
		var builder = new FieldBuilder();
		FieldBuilder result = builder.Keyword().Normalizer("keyword_normalizer").CopyTo("content_tags");

		var definition = (KeywordFieldDefinition)result.GetDefinition();
		definition.Normalizer.Should().Be("keyword_normalizer");
		definition.CopyTo.Should().Be("content_tags");
	}
}
