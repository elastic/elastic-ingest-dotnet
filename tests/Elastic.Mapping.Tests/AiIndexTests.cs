// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Elastic.Mapping.Mappings;

namespace Elastic.Mapping.Tests;

public class AiIndexTestDocument
{
	[Id]
	[Keyword]
	[JsonPropertyName("id")]
	public string Id { get; set; } = string.Empty;

	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	[JsonPropertyName("content")]
	public string Body { get; set; } = string.Empty;

	// Defined without the lowercase normalizer that ai-index@mappings puts on tags.
	[Keyword]
	[JsonPropertyName("tags")]
	public string[] Tags { get; set; } = [];

	[Keyword]
	[JsonPropertyName("section")]
	public string Section { get; set; } = string.Empty;
}

[ElasticsearchMappingContext]
[Index<AiIndexTestDocument>(Name = "ai-index-idx-test-on", AiIndex = true, Variant = "On")]
[Index<AiIndexTestDocument>(Name = "ai-index-idx-test-off", Variant = "Off")]
[Index<AiIndexTestDocument>(Name = "ai-index-idx-test-configured", AiIndex = true, Variant = "Configured")]
[DataStream<AiIndexTestDocument>(Type = "logs", Dataset = "ai-index", Namespace = "test", AiIndex = true, Variant = "Stream")]
public static partial class AiIndexMappingContext
{
	public static MappingsBuilder<AiIndexTestDocument> ConfigureAiIndexTestDocumentConfiguredMappings(
		MappingsBuilder<AiIndexTestDocument> mappings) =>
		mappings.AddField("title.exact", f => f.Keyword());
}

public class AiIndexTests
{
	private static JsonNode Mappings(ElasticsearchTypeContext context) =>
		JsonNode.Parse(context.GetMappingsJson())!;

	/// <summary>Type of the field at <paramref name="path"/>, following sub-properties and multi-fields.</summary>
	private static string? TypeOf(JsonNode mappings, params string[] path)
	{
		JsonNode? node = mappings;
		foreach (var segment in path)
			node = node?["properties"]?[segment] ?? node?["fields"]?[segment];
		return node?["type"]?.GetValue<string>();
	}

	[Test]
	public void AiIndex_AddsEveryBaseField()
	{
		var mappings = Mappings(AiIndexMappingContext.AiIndexTestDocumentOn.Context);

		foreach (var field in new[]
		{
			"@timestamp", "id", "type", "title", "description", "content", "tags", "attributes",
			"updated_at", "expires_at", "references", "governance"
		})
			mappings["properties"]![field].Should().NotBeNull($"'{field}' is part of ai-index@mappings");

		TypeOf(mappings, "references", "uri").Should().Be("keyword");
		TypeOf(mappings, "governance", "provenance", "created_by", "uri").Should().Be("keyword");
		TypeOf(mappings, "governance", "provenance", "updated_by", "metadata").Should().Be("flattened");
		TypeOf(mappings, "governance", "lifecycle", "status").Should().Be("keyword");
	}

	[Test]
	public void AiIndex_AddsSemanticMultiFieldToFieldsTheDocumentAlreadyMaps()
	{
		var mappings = Mappings(AiIndexMappingContext.AiIndexTestDocumentOn.Context);

		// title and content come from the document type and keep what the generator gives them.
		TypeOf(mappings, "title").Should().Be("text");
		TypeOf(mappings, "title", "keyword").Should().Be("keyword");
		TypeOf(mappings, "title", "semantic").Should().Be("semantic_text");
		TypeOf(mappings, "content", "semantic").Should().Be("semantic_text");
		// description comes from the base mapping alone.
		TypeOf(mappings, "description", "semantic").Should().Be("semantic_text");
	}

	[Test]
	public void AiIndex_KeepsFieldsOfTheDocumentType()
	{
		var mappings = Mappings(AiIndexMappingContext.AiIndexTestDocumentOn.Context);

		TypeOf(mappings, "section").Should().Be("keyword");
	}

	[Test]
	public void AiIndex_NeverChangesAFieldTheDocumentDefines()
	{
		var tags = Mappings(AiIndexMappingContext.AiIndexTestDocumentOn.Context)["properties"]!["tags"]!;

		tags["type"]!.GetValue<string>().Should().Be("keyword");
		tags["normalizer"].Should().BeNull("the document's own definition wins over the base mapping");
	}

	[Test]
	public void AiIndex_Off_AddsNoBaseFields()
	{
		var mappings = Mappings(AiIndexMappingContext.AiIndexTestDocumentOff.Context);

		mappings["properties"]!["governance"].Should().BeNull();
		mappings["properties"]!["updated_at"].Should().BeNull();
		TypeOf(mappings, "title", "semantic").Should().BeNull();
	}

	[Test]
	public void AiIndex_FlowsToContext()
	{
		AiIndexMappingContext.AiIndexTestDocumentOn.Context.AiIndex.Should().BeTrue();
		AiIndexMappingContext.AiIndexTestDocumentStream.Context.AiIndex.Should().BeTrue();
		AiIndexMappingContext.AiIndexTestDocumentOff.Context.AiIndex.Should().BeFalse();
	}

	[Test]
	public void AiIndex_ChangesTheMappingsHash_SoBootstrapRewritesTheTemplate()
	{
		var on = AiIndexMappingContext.AiIndexTestDocumentOn.Context;
		var off = AiIndexMappingContext.AiIndexTestDocumentOff.Context;

		on.MappingsHash.Should().NotBe(off.MappingsHash);
		on.Hash.Should().NotBe(off.Hash);
	}

	[Test]
	public void AiIndex_AppliesToDataStreams()
	{
		var mappings = Mappings(AiIndexMappingContext.AiIndexTestDocumentStream.Context);

		TypeOf(mappings, "governance", "lifecycle", "status").Should().Be("keyword");
		TypeOf(mappings, "title", "semantic").Should().Be("semantic_text");
	}

	[Test]
	public void AiIndex_WithConfigureMappings_KeepsTheConfiguredFieldsAndAddsTheBase()
	{
		var mappings = Mappings(AiIndexMappingContext.AiIndexTestDocumentConfigured.Context);

		TypeOf(mappings, "title", "exact").Should().Be("keyword");
		TypeOf(mappings, "title", "semantic").Should().Be("semantic_text");
		TypeOf(mappings, "governance", "provenance", "created_by", "uri").Should().Be("keyword");
	}

	[Test]
	public void BaseMappingJson_MatchesTheComponentTemplateFieldList()
	{
		var properties = JsonNode.Parse(AiIndexMapping.BaseMappingJson)!["properties"]!.AsObject();

		properties.Select(p => p.Key).Should().BeEquivalentTo(
			"@timestamp", "id", "type", "title", "description", "content", "tags", "attributes",
			"updated_at", "expires_at", "references", "governance");
	}

	[Test]
	public void MergeInto_IsIdempotent()
	{
		var once = AiIndexMapping.MergeInto("{\"properties\":{\"name\":{\"type\":\"keyword\"}}}");

		AiIndexMapping.MergeInto(once).Should().Be(once);
	}
}
