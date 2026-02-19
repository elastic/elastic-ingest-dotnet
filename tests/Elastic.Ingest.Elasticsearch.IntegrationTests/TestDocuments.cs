// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Text.Json.Serialization;
using Elastic.Mapping;
using Elastic.Mapping.Analysis;
using Elastic.Mapping.Mappings;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

/// <summary>
/// Simulates an application observability event ingested into data streams.
/// Realistic fields: timestamp, severity, service identity, request tracing, and log text.
/// </summary>
public partial class ServerMetricsEvent : IConfigureElasticsearch<ServerMetricsEventMappingsBuilder>
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

	public static AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		analysis
			.Analyzer("log_message", a => a.Custom()
				.Tokenizer("standard")
				.Filters("lowercase", "asciifolding"));

	public static ServerMetricsEventMappingsBuilder ConfigureMappings(ServerMetricsEventMappingsBuilder mappings) =>
		mappings
			.AddRuntimeField("is_slow", f => f.Boolean().Script("emit(doc['duration_ms'].value > 1000)"));
}

/// <summary>
/// Simulates a product catalog entry indexed into a regular index.
/// Realistic fields: product identity, searchable text, pricing, categorization, change tracking.
/// </summary>
public partial class ProductCatalog : IConfigureElasticsearch<ProductCatalogMappingsBuilder>
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

	[Keyword]
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

	public static AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		analysis
			.TokenFilter("edge_ngram_filter", t => t.EdgeNGram()
				.MinGram(2)
				.MaxGram(15))
			.Analyzer("product_autocomplete", a => a.Custom()
				.Tokenizer("standard")
				.Filters("lowercase", "edge_ngram_filter"));

	public static ProductCatalogMappingsBuilder ConfigureMappings(ProductCatalogMappingsBuilder mappings) =>
		mappings
			.AddRuntimeField("price_tier", f => f.Keyword()
				.Script("if (doc['price'].value < 10) emit('budget'); else if (doc['price'].value < 100) emit('mid'); else emit('premium')"));
}

// --- Legacy document types used by old channel tests (ScriptedHash, CustomScript, Semantic) ---

public class TimeSeriesDocument
{
	[JsonPropertyName("@timestamp")]
	public DateTimeOffset Timestamp { get; set; }

	[JsonPropertyName("message")]
	public string? Message { get; set; }
}

public class CatalogDocument
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = null!;

	[JsonPropertyName("title")]
	public string? Title { get; set; }

	[JsonPropertyName("created")]
	public DateTimeOffset Created { get; set; }
}

public class HashDocument
{
	[JsonPropertyName("id")]
	public string Id { get; set; } = null!;

	[JsonPropertyName("title")]
	public string? Title { get; set; }

	[JsonPropertyName("hash")]
	public string Hash { get; set; } = string.Empty;

	[JsonPropertyName("index_batch_date")]
	public DateTimeOffset IndexBatchDate { get; set; }

	[JsonPropertyName("last_updated")]
	public DateTimeOffset LastUpdated { get; set; }
}
