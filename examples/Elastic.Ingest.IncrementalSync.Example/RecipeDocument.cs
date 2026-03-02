// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Mapping;

namespace Elastic.Ingest.IncrementalSync.Example;

/// <summary>
/// A recipe from a community cookbook — indexed to both lexical and semantic indices,
/// with AI enrichment for auto-generated tags and summaries.
/// </summary>
public class RecipeDocument
{
	[Id]
	[Keyword]
	[JsonPropertyName("slug")]
	public string Slug { get; set; } = string.Empty;

	[AiInput]
	[Text(Analyzer = "standard")]
	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	[AiInput]
	[Text(Analyzer = "standard")]
	[JsonPropertyName("description")]
	public string Description { get; set; } = string.Empty;

	[Keyword]
	[JsonPropertyName("cuisine")]
	public string Cuisine { get; set; } = string.Empty;

	[Keyword]
	[JsonPropertyName("ingredients")]
	public string[] Ingredients { get; set; } = [];

	[Long]
	[JsonPropertyName("prep_time_minutes")]
	public int PrepTimeMinutes { get; set; }

	[Long]
	[JsonPropertyName("cook_time_minutes")]
	public int CookTimeMinutes { get; set; }

	[Long]
	[JsonPropertyName("servings")]
	public int Servings { get; set; }

	[AiField("A concise one-sentence summary of this recipe suitable for search results.")]
	[Text]
	[JsonPropertyName("ai_summary")]
	public string? AiSummary { get; set; }

	[AiField("3 to 6 dietary or category tags for this recipe (e.g. vegetarian, gluten-free, quick-meal, comfort-food).", MinItems = 3, MaxItems = 6)]
	[Keyword]
	[JsonPropertyName("ai_tags")]
	public string[]? AiTags { get; set; }

	[ContentHash]
	[Keyword]
	[JsonPropertyName("content_hash")]
	public string ContentHash { get; set; } = string.Empty;

	[BatchIndexDate]
	[Date]
	[JsonPropertyName("batch_index_date")]
	public DateTimeOffset BatchIndexDate { get; set; }

	[LastUpdated]
	[Date]
	[JsonPropertyName("last_updated")]
	public DateTimeOffset LastUpdated { get; set; }
}
