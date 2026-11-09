// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Elastic.Mapping.Tests;

/// <summary>The common case: the base class plus a field of your own.</summary>
public class AiArticle : AiIndexDocument
{
	[Keyword]
	[JsonPropertyName("author")]
	public string? Author { get; set; }
}

/// <summary>
/// Overrides base properties to change their mapping and to add a role. An override repeats
/// <c>[JsonPropertyName]</c>, because the attributes of the most derived property are the ones that count.
/// </summary>
public class AiAnalyzedArticle : AiIndexDocument
{
	[Text(Analyzer = "standard")]
	[JsonPropertyName("title")]
	public override string? Title { get; set; }

	[Timestamp]
	[JsonPropertyName("@timestamp")]
	public override DateTimeOffset Timestamp { get; set; }
}

/// <summary>Brings its own document id, for example a URL, instead of the base <c>id</c>.</summary>
public class AiPage : AiIndexDocument
{
	[Id]
	[Keyword]
	[JsonPropertyName("path")]
	public string Path { get; set; } = string.Empty;
}

[ElasticsearchMappingContext]
[Index<AiArticle>(Name = "ai-index-idx-articles", AiIndex = true)]
[Index<AiArticle>(Name = "articles-without-ai-index", Variant = "WithoutAiIndex")]
[Index<AiAnalyzedArticle>(Name = "ai-index-idx-analyzed", AiIndex = true)]
[Index<AiPage>(Name = "ai-index-idx-pages", AiIndex = true)]
[DataStream<AiAnalyzedArticle>(Type = "logs", Dataset = "ai-analyzed", Namespace = "test", AiIndex = true, Variant = "Stream")]
public static partial class AiIndexDocumentContext
{
	public static Mappings.MappingsBuilder<AiArticle> ConfigureAiArticleMappings(Mappings.MappingsBuilder<AiArticle> mappings) =>
		mappings.AddField("title.exact", f => f.Keyword());
}

public class AiIndexDocumentTests
{
	private static JsonNode Mappings(ElasticsearchTypeContext context) =>
		JsonNode.Parse(context.GetMappingsJson())!;

	/// <summary>The definition of the field at <paramref name="path"/>, following sub-properties and multi-fields.</summary>
	private static JsonNode? Field(JsonNode mappings, params string[] path)
	{
		JsonNode? node = mappings;
		foreach (var segment in path)
			node = node?["properties"]?[segment] ?? node?["fields"]?[segment];
		return node;
	}

	private static void Covers(JsonObject expected, JsonObject? actual, string path, List<string> problems)
	{
		foreach (var (name, expectedDef) in expected)
		{
			var fieldPath = path + name;
			var actualDef = actual?[name];
			if (actualDef == null)
			{
				problems.Add($"{fieldPath}: missing");
				continue;
			}

			foreach (var key in new[] { "type", "normalizer" })
			{
				var want = expectedDef![key]?.GetValue<string>();
				var have = actualDef[key]?.GetValue<string>();
				if (want != have)
					problems.Add($"{fieldPath}.{key}: expected '{want}', found '{have}'");
			}

			if (expectedDef!["properties"]?.AsObject() is { } children)
				Covers(children, actualDef["properties"]?.AsObject(), fieldPath + ".", problems);
		}
	}

	[Test]
	public void SubclassMapsEveryBaseFieldWithTheTypeOfTheBaseMappingEvenWithoutAiIndex()
	{
		// Without AiIndex nothing is merged in, so this is the base class alone: it must not drift from ai-index@mappings.
		var mappings = Mappings(AiIndexDocumentContext.AiArticleWithoutAiIndex.Context);
		var expected = JsonNode.Parse(AiIndexMapping.BaseMappingJson)!["properties"]!.AsObject();
		var problems = new List<string>();

		Covers(expected, mappings["properties"]!.AsObject(), string.Empty, problems);

		// `attributes` and the metadata fields are flattened, and the lowercase normalizers are on type and tags.
		problems.Should().BeEmpty();
	}

	[Test]
	public void SubclassWithAiIndexAddsTheSemanticMultiFields()
	{
		var mappings = Mappings(AiIndexDocumentContext.AiArticle.Context);

		Field(mappings, "title", "semantic")!["type"]!.GetValue<string>().Should().Be("semantic_text");
		Field(mappings, "description", "semantic")!["type"]!.GetValue<string>().Should().Be("semantic_text");
		Field(mappings, "content", "semantic")!["type"]!.GetValue<string>().Should().Be("semantic_text");
	}

	[Test]
	public void SubclassWithoutAiIndexHasNoSemanticMultiFields()
	{
		var mappings = Mappings(AiIndexDocumentContext.AiArticleWithoutAiIndex.Context);

		Field(mappings, "title", "semantic").Should().BeNull();
	}

	[Test]
	public void SubclassFieldsAreMappedNextToTheBase()
	{
		var mappings = Mappings(AiIndexDocumentContext.AiArticle.Context);

		Field(mappings, "author")!["type"]!.GetValue<string>().Should().Be("keyword");
		Field(mappings, "governance", "provenance", "created_by", "uri")!["type"]!.GetValue<string>().Should().Be("keyword");
	}

	[Test]
	public void ConfigureMappingsExtendsABaseFieldAndKeepsTheSemanticMultiField()
	{
		var mappings = Mappings(AiIndexDocumentContext.AiArticle.Context);

		Field(mappings, "title", "exact")!["type"]!.GetValue<string>().Should().Be("keyword");
		Field(mappings, "title", "keyword")!["type"]!.GetValue<string>().Should().Be("keyword");
		Field(mappings, "title", "semantic")!["type"]!.GetValue<string>().Should().Be("semantic_text");
	}

	[Test]
	public void OverridingAPropertyChangesItsMapping()
	{
		var mappings = Mappings(AiIndexDocumentContext.AiAnalyzedArticle.Context);

		Field(mappings, "title")!["analyzer"]!.GetValue<string>().Should().Be("standard");
		Field(mappings, "title", "semantic")!["type"]!.GetValue<string>().Should().Be("semantic_text");
		mappings["properties"]!.AsObject().Count(p => p.Key == "title").Should().Be(1);
	}

	[Test]
	public void SubclassIdRoleWinsOverTheBaseId()
	{
		var context = AiIndexDocumentContext.AiPage.Context;

		context.GetId!(new AiPage { Path = "/docs/page", Id = "other" }).Should().Be("/docs/page");
	}

	[Test]
	public void BaseIdIsTheDocumentIdWhenTheSubclassHasNoIdOfItsOwn()
	{
		var context = AiIndexDocumentContext.AiArticle.Context;

		context.GetId!(new AiArticle { Id = "article-1" }).Should().Be("article-1");
	}

	[Test]
	public void SubclassCanAddTheTimestampRoleForADataStream()
	{
		var context = AiIndexDocumentContext.AiAnalyzedArticleStream.Context;
		var written = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

		context.AiIndex.Should().BeTrue();
		context.GetTimestamp!(new AiAnalyzedArticle { Timestamp = written }).Should().Be(written);
		Field(Mappings(context), "@timestamp")!["type"]!.GetValue<string>().Should().Be("date");
	}

	[Test]
	public void IndexJsonCarriesTheBaseFieldsOfASubclass()
	{
		var index = JsonNode.Parse(AiIndexDocumentContext.AiArticle.Context.GetIndexJson())!;

		Field(index["mappings"]!, "governance", "lifecycle", "status").Should().NotBeNull();
	}

	[Test]
	public void ASubclassSerializesWithTheJsonNamesOfTheBaseMappingAndOmitsEmptyFields()
	{
		var written = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
		var article = new AiArticle
		{
			Timestamp = written,
			Id = "article-1",
			Title = "A title",
			Tags = ["Docs"],
			Attributes = new() { ["product"] = "elasticsearch" },
			References = [new() { Uri = "https://example.com", Relation = "derived_from" }],
			Governance = new()
			{
				Provenance = new() { CreatedBy = new() { Uri = "workflow://sync", Metadata = new() { ["run_id"] = "r1" } } },
				Lifecycle = new() { Status = "active" }
			},
			Author = "someone"
		};

		var json = JsonNode.Parse(JsonSerializer.Serialize(article))!;

		json["@timestamp"]!.GetValue<DateTimeOffset>().Should().Be(written);
		json["id"]!.GetValue<string>().Should().Be("article-1");
		json["tags"]![0]!.GetValue<string>().Should().Be("Docs");
		json["attributes"]!["product"]!.GetValue<string>().Should().Be("elasticsearch");
		json["references"]![0]!["relation"]!.GetValue<string>().Should().Be("derived_from");
		json["governance"]!["provenance"]!["created_by"]!["uri"]!.GetValue<string>().Should().Be("workflow://sync");
		json["governance"]!["provenance"]!["created_by"]!["metadata"]!["run_id"]!.GetValue<string>().Should().Be("r1");
		json["governance"]!["lifecycle"]!["status"]!.GetValue<string>().Should().Be("active");
		json["author"]!.GetValue<string>().Should().Be("someone");
		json.AsObject().ContainsKey("description").Should().BeFalse("empty fields are not written");
		json.AsObject().ContainsKey("expires_at").Should().BeFalse();
	}

	[Test]
	public void TimestampDefaultsToWhenTheDocumentIsCreated()
	{
		var before = DateTimeOffset.UtcNow;
		var article = new AiArticle();

		article.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTimeOffset.UtcNow);
	}
}
