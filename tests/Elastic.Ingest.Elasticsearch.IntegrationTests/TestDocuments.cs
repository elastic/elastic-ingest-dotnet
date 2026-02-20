// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Text.Json.Serialization;
using Elastic.Mapping;
using Elastic.Mapping.Analysis;
using Elastic.Mapping.Mappings;
using static Elastic.Mapping.Analysis.BuiltInAnalysis;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

// ---------------------------------------------------------------------------
// Domain POCOs — clean types with field-level Elastic.Mapping attributes only.
// Analysis and mapping customization live in separate static Configuration
// classes referenced via Configuration = typeof(...) on the entity attribute.
// ---------------------------------------------------------------------------

/// <summary>
/// Simulates an application observability event ingested into data streams.
/// </summary>
public partial class ServerMetricsEvent
{
	[Timestamp]
	[JsonPropertyName("@timestamp")]
	public DateTimeOffset Timestamp { get; set; }

	[Text(Analyzer = "log_message")]
	[JsonPropertyName("message")]
	public string Message { get; set; } = string.Empty;

	[Keyword]
	[JsonPropertyName("log_level")]
	public string LogLevel { get; set; } = "info";

	[Keyword]
	[JsonPropertyName("service_name")]
	public string ServiceName { get; set; } = string.Empty;

	[Keyword]
	[JsonPropertyName("host_name")]
	public string HostName { get; set; } = string.Empty;

	[Keyword]
	[JsonPropertyName("trace_id")]
	public string? TraceId { get; set; }

	[Long]
	[JsonPropertyName("duration_ms")]
	public long DurationMs { get; set; }
}

public static class ServerMetricsEventConfig
{
	public static AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		analysis
			.Analyzer("log_message", a => a.Custom()
				.Tokenizer(Tokenizers.Standard)
				.Filters(TokenFilters.Lowercase, TokenFilters.AsciiFolding));

	public static ServerMetricsEventMappingsBuilder ConfigureMappings(ServerMetricsEventMappingsBuilder mappings) =>
		mappings
			.AddRuntimeField("is_slow", f => f.Boolean().Script("emit(doc['duration_ms'].value > 1000)"));
}

/// <summary>
/// Simulates a product catalog entry indexed into a regular index.
/// </summary>
public partial class ProductCatalog
{
	[Id]
	[Keyword]
	[JsonPropertyName("sku")]
	public string Sku { get; set; } = null!;

	[Text(Analyzer = "product_autocomplete", SearchAnalyzer = "standard")]
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[Text]
	[JsonPropertyName("description")]
	public string Description { get; set; } = string.Empty;

	[Keyword(Normalizer = "lowercase_ascii")]
	[JsonPropertyName("category")]
	public string Category { get; set; } = string.Empty;

	[Double]
	[JsonPropertyName("price")]
	public double Price { get; set; }

	[Keyword]
	[JsonPropertyName("tags")]
	public string[] Tags { get; set; } = [];

	[ContentHash]
	[Keyword]
	[JsonPropertyName("content_hash")]
	public string ContentHash { get; set; } = string.Empty;

	[Date]
	[JsonPropertyName("updated_at")]
	public DateTimeOffset UpdatedAt { get; set; }
}

public static class ProductCatalogConfig
{
	public static AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		analysis
			.Normalizer("lowercase_ascii", n => n.Custom()
				.Filters(TokenFilters.Lowercase, TokenFilters.AsciiFolding))
			.TokenFilter("edge_ngram_filter", t => t.EdgeNGram()
				.MinGram(2)
				.MaxGram(15))
			.Analyzer("product_autocomplete", a => a.Custom()
				.Tokenizer(Tokenizers.Standard)
				.Filters(TokenFilters.Lowercase, "edge_ngram_filter"));

	public static ProductCatalogMappingsBuilder ConfigureMappings(ProductCatalogMappingsBuilder mappings) =>
		mappings
			.AddRuntimeField("price_tier", f => f.Keyword()
				.Script("if (doc['price'].value < 10) emit('budget'); else if (doc['price'].value < 100) emit('mid'); else emit('premium')"));
}

/// <summary>
/// Simulates a document with content-hash tracking for scripted bulk upserts.
/// </summary>
public partial class HashableArticle
{
	[Id]
	[Keyword]
	[JsonPropertyName("id")]
	public string Id { get; set; } = null!;

	[Text(Analyzer = "html_content")]
	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	[ContentHash]
	[Keyword]
	[JsonPropertyName("hash")]
	public string Hash { get; set; } = string.Empty;

	[Date]
	[JsonPropertyName("index_batch_date")]
	public DateTimeOffset IndexBatchDate { get; set; }

	[Date]
	[JsonPropertyName("last_updated")]
	public DateTimeOffset LastUpdated { get; set; }
}

public static class HashableArticleConfig
{
	public static AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		analysis
			.CharFilter("html_stripper", c => c.HtmlStrip())
			.Analyzer("html_content", a => a.Custom()
				.Tokenizer(Tokenizers.Standard)
				.CharFilters("html_stripper")
				.Filters(TokenFilters.Lowercase, TokenFilters.AsciiFolding));

	public static HashableArticleMappingsBuilder ConfigureMappings(HashableArticleMappingsBuilder mappings) =>
		mappings.Title(f => f.MultiField("keyword", mf => mf.Keyword().IgnoreAbove(256)));
}

/// <summary>
/// Simulates a document for semantic search with inference endpoints.
/// </summary>
public partial class SemanticArticle
{
	[Id]
	[Keyword]
	[JsonPropertyName("id")]
	public string Id { get; set; } = null!;

	[Text(Analyzer = "semantic_content")]
	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	[SemanticText(InferenceId = "test-elser-inference")]
	[JsonPropertyName("semantic_text")]
	public string SemanticContent { get; set; } = string.Empty;

	[Date]
	[JsonPropertyName("created")]
	public DateTimeOffset Created { get; set; }
}

public static class SemanticArticleConfig
{
	public static AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		analysis
			.Analyzer("semantic_content", a => a.Custom()
				.Tokenizer(Tokenizers.Standard)
				.Filters(TokenFilters.Lowercase, TokenFilters.AsciiFolding));

	public static SemanticArticleMappingsBuilder ConfigureMappings(SemanticArticleMappingsBuilder mappings) =>
		mappings.Title(f => f.MultiField("keyword", mf => mf.Keyword().IgnoreAbove(256)));
}
