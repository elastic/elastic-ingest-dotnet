// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Elastic.Mapping;
using Elastic.Mapping.Analysis;
using Elastic.Mapping.Mappings;
using static Elastic.Mapping.Analysis.BuiltInAnalysis;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

// ---------------------------------------------------------------------------
// Domain POCOs — clean types with field-level Elastic.Mapping attributes only.
// Analysis and mapping customization live in separate Configuration classes
// implementing IConfigureElasticsearch<T>, referenced via Configuration = typeof(...)
// on the entity attribute.
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

public class ServerMetricsEventConfig : IConfigureElasticsearch<ServerMetricsEvent>
{
	public IReadOnlyDictionary<string, string> IndexSettings => new Dictionary<string, string>
	{
		["index.default_pipeline"] = "logs-default-pipeline"
	};

	public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		analysis
			.Analyzer("log_message", a => a.Custom()
				.Tokenizer(Tokenizers.Standard)
				.Filters(TokenFilters.Lowercase, TokenFilters.AsciiFolding));

	public MappingsBuilder<ServerMetricsEvent> ConfigureMappings(MappingsBuilder<ServerMetricsEvent> mappings) =>
		mappings
			.AddRuntimeField("is_slow", f => f.Boolean().Script("emit(doc['duration_ms'].value > 1000)"));
}

/// <summary>
/// V2 of ServerMetricsEvent — adds an error_code field, producing a different
/// mapping hash from V1 so the channel detects template changes.
/// </summary>
public partial class ServerMetricsEventV2 : ServerMetricsEvent
{
	[Keyword]
	[JsonPropertyName("error_code")]
	public string? ErrorCode { get; set; }
}

/// <summary>
/// V2 configuration — adds a stop-words filter to the log_message analyzer
/// and replaces is_slow with is_error runtime field.
/// </summary>
public class ServerMetricsEventV2Config : IConfigureElasticsearch<ServerMetricsEventV2>
{
	public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		analysis
			.Analyzer("log_message", a => a.Custom()
				.Tokenizer(Tokenizers.Standard)
				.Filters(TokenFilters.Lowercase, TokenFilters.AsciiFolding, TokenFilters.Stop));

	public MappingsBuilder<ServerMetricsEventV2> ConfigureMappings(MappingsBuilder<ServerMetricsEventV2> mappings) =>
		mappings
			.AddRuntimeField("is_error", f => f.Boolean().Script("emit(doc['log_level.keyword'].size() > 0 && doc['log_level.keyword'].value == 'error')"));
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

public class ProductCatalogConfig : IConfigureElasticsearch<ProductCatalog>
{
	public IReadOnlyDictionary<string, string> IndexSettings => new Dictionary<string, string>
	{
		["index.default_pipeline"] = "products-default-pipeline"
	};

	public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		analysis
			.Normalizer("lowercase_ascii", n => n.Custom()
				.Filters(TokenFilters.Lowercase, TokenFilters.AsciiFolding))
			.TokenFilter("edge_ngram_filter", t => t.EdgeNGram()
				.MinGram(2)
				.MaxGram(15))
			.Analyzer("product_autocomplete", a => a.Custom()
				.Tokenizer(Tokenizers.Standard)
				.Filters(TokenFilters.Lowercase, "edge_ngram_filter"));

	public MappingsBuilder<ProductCatalog> ConfigureMappings(MappingsBuilder<ProductCatalog> mappings) =>
		mappings
			.AddRuntimeField("price_tier", f => f.Keyword()
				.Script("if (doc['price'].value < 10) emit('budget'); else if (doc['price'].value < 100) emit('mid'); else emit('premium')"));
}

/// <summary>
/// V2 of ProductCatalog — adds an is_featured field, producing a different
/// mapping hash from V1 so the channel detects template changes.
/// </summary>
public partial class ProductCatalogV2 : ProductCatalog
{
	[Boolean]
	[JsonPropertyName("is_featured")]
	public bool IsFeatured { get; set; }
}

/// <summary>
/// V2 configuration — widens the edge_ngram window (3..20), adds a stop-words filter,
/// and replaces price_tier with discount_eligible runtime field.
/// </summary>
public class ProductCatalogV2Config : IConfigureElasticsearch<ProductCatalogV2>
{
	public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		analysis
			.Normalizer("lowercase_ascii", n => n.Custom()
				.Filters(TokenFilters.Lowercase, TokenFilters.AsciiFolding))
			.TokenFilter("edge_ngram_filter", t => t.EdgeNGram()
				.MinGram(3)
				.MaxGram(20))
			.Analyzer("product_autocomplete", a => a.Custom()
				.Tokenizer(Tokenizers.Standard)
				.Filters(TokenFilters.Lowercase, TokenFilters.Stop, "edge_ngram_filter"));

	public MappingsBuilder<ProductCatalogV2> ConfigureMappings(MappingsBuilder<ProductCatalogV2> mappings) =>
		mappings
			.AddRuntimeField("discount_eligible", f => f.Boolean()
				.Script("emit(doc['price'].value < 25)"));
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

	[BatchIndexDate]
	[Date]
	[JsonPropertyName("index_batch_date")]
	public DateTimeOffset IndexBatchDate { get; set; }

	[LastUpdated]
	[Date]
	[JsonPropertyName("last_updated")]
	public DateTimeOffset LastUpdated { get; set; }
}

public class HashableArticleConfig : IConfigureElasticsearch<HashableArticle>
{
	public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		analysis
			.CharFilter("html_stripper", c => c.HtmlStrip())
			.Analyzer("html_content", a => a.Custom()
				.Tokenizer(Tokenizers.Standard)
				.CharFilters("html_stripper")
				.Filters(TokenFilters.Lowercase, TokenFilters.AsciiFolding));

	public MappingsBuilder<HashableArticle> ConfigureMappings(MappingsBuilder<HashableArticle> mappings) =>
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

public class SemanticArticleConfig : IConfigureElasticsearch<SemanticArticle>
{
	public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		analysis
			.Analyzer("semantic_content", a => a.Custom()
				.Tokenizer(Tokenizers.Standard)
				.Filters(TokenFilters.Lowercase, TokenFilters.AsciiFolding));

	public MappingsBuilder<SemanticArticle> ConfigureMappings(MappingsBuilder<SemanticArticle> mappings) =>
		mappings.Title(f => f.MultiField("keyword", mf => mf.Keyword().IgnoreAbove(256)));
}
