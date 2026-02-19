// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Linq;
using System.Text.Json;
using Elastic.Mapping;
using Elastic.Mapping.Analysis;
using FluentAssertions;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Mapping;

/*
 * Tests: Source-generator output verification for Elastic.Mapping
 *
 * No Elasticsearch cluster required — these are pure unit tests that validate
 * the compile-time artefacts produced by the Elastic.Mapping source generator:
 *
 *   TestMappingContext
 *   ├── ServerMetricsEvent   (DataStream: logs-srvmetrics-default)
 *   │   ├── GetMappingsJson  → @timestamp, message (analyzer: log_message), log_level, ...
 *   │   ├── GetSettingsJson  → {} (no entity-level settings)
 *   │   ├── ConfigureAnalysis→ log_message analyzer (standard + lowercase + asciifolding)
 *   │   └── ConfigureMappings→ is_slow runtime field
 *   │
 *   ├── ProductCatalog       (Index: idx-products)
 *   │   ├── GetMappingsJson  → sku, name (analyzer: product_autocomplete), category (normalizer: lowercase_ascii), ...
 *   │   ├── GetSettingsJson  → { refresh_interval: "1s" }
 *   │   ├── ConfigureAnalysis→ product_autocomplete analyzer + edge_ngram_filter + lowercase_ascii normalizer
 *   │   └── ConfigureMappings→ price_tier runtime field
 *   │
 *   ├── ProductCatalogCatalog (Index: cat-products, aliased + date-rolling)
 *   │   └── Same mappings as ProductCatalog, different entity settings → different hash
 *   │
 *   ├── HashableArticle      (Index: hashable-articles)
 *   │   ├── GetMappingsJson  → id, title (analyzer: html_content), hash ([ContentHash]), ...
 *   │   ├── ConfigureAnalysis→ html_content analyzer (standard + html_stripper + lowercase + asciifolding)
 *   │   └── ConfigureMappings→ title.keyword multi-field
 *   │
 *   └── SemanticArticle      (Index: semantic-articles)
 *       ├── GetMappingsJson  → id, title (analyzer: semantic_content), semantic_text (inference), ...
 *       ├── ConfigureAnalysis→ semantic_content analyzer (standard + lowercase + asciifolding)
 *       └── ConfigureMappings→ title.keyword multi-field
 */
public class ElasticMappingTests
{
	// --- ServerMetricsEvent (DataStream) ---

	[Test]
	public void ServerMetricsEventContextHasCorrectEntityTarget()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		ctx.EntityTarget.Should().Be(EntityTarget.DataStream);
	}

	[Test]
	public void ServerMetricsEventContextHasDataStreamName()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		ctx.IndexStrategy!.DataStreamName.Should().Be("logs-srvmetrics-default");
		ctx.IndexStrategy.Type.Should().Be("logs");
		ctx.IndexStrategy.Dataset.Should().Be("srvmetrics");
		ctx.IndexStrategy.Namespace.Should().Be("default");
	}

	[Test]
	public void ServerMetricsEventContextHasTimestampDelegate()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		ctx.GetTimestamp.Should().NotBeNull("ServerMetricsEvent has [Timestamp] on its Timestamp property");

		var now = DateTimeOffset.UtcNow;
		var evt = new ServerMetricsEvent { Timestamp = now };
		ctx.GetTimestamp!(evt).Should().Be(now);
	}

	[Test]
	public void ServerMetricsEventMappingsJsonContainsAllFields()
	{
		var json = TestMappingContext.ServerMetricsEvent.Context.GetMappingsJson!();
		using var doc = JsonDocument.Parse(json);
		var props = doc.RootElement.GetProperty("properties");

		props.GetProperty("@timestamp").GetProperty("type").GetString().Should().Be("date");
		props.GetProperty("message").GetProperty("type").GetString().Should().Be("text");
		props.GetProperty("message").GetProperty("analyzer").GetString().Should().Be("log_message");
		props.GetProperty("log_level").GetProperty("type").GetString().Should().Be("keyword");
		props.GetProperty("service_name").GetProperty("type").GetString().Should().Be("keyword");
		props.GetProperty("host_name").GetProperty("type").GetString().Should().Be("keyword");
		props.GetProperty("trace_id").GetProperty("type").GetString().Should().Be("keyword");
		props.GetProperty("duration_ms").GetProperty("type").GetString().Should().Be("long");
	}

	[Test]
	public void ServerMetricsEventSettingsJsonIsEmpty()
	{
		var json = TestMappingContext.ServerMetricsEvent.Context.GetSettingsJson!();
		using var doc = JsonDocument.Parse(json);
		doc.RootElement.EnumerateObject().Should().BeEmpty(
			"data stream entity has no explicit shards/replicas/refresh_interval");
	}

	[Test]
	public void ServerMetricsEventConfigureAnalysisIsWiredUp()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		ctx.ConfigureAnalysis.Should().NotBeNull(
			"ServerMetricsEvent implements IConfigureElasticsearch with ConfigureAnalysis");
	}

	[Test]
	public void ServerMetricsEventConfigureAnalysisProducesLogMessageAnalyzer()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var builder = new AnalysisBuilder();
		var result = ctx.ConfigureAnalysis!(builder);
		result.HasConfiguration.Should().BeTrue();

		var settings = result.Build();
		var analysisJson = settings.ToJsonString();
		using var doc = JsonDocument.Parse(analysisJson);
		var analyzer = doc.RootElement.GetProperty("analyzer").GetProperty("log_message");
		analyzer.GetProperty("type").GetString().Should().Be("custom");
		analyzer.GetProperty("tokenizer").GetString().Should().Be("standard");
		analyzer.GetProperty("filter").EnumerateArray().Select(e => e.GetString())
			.Should().ContainInOrder("lowercase", "asciifolding");
	}

	[Test]
	public void ServerMetricsEventConfigureMappingsProducesRuntimeField()
	{
		var builder = new ServerMetricsEventMappingsBuilder();
		var result = ServerMetricsEvent.ConfigureMappings(builder);
		result.HasConfiguration.Should().BeTrue(
			"ConfigureMappings adds an is_slow runtime field");
	}

	// --- ProductCatalog (Index) ---

	[Test]
	public void ProductCatalogContextHasCorrectEntityTarget()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		ctx.EntityTarget.Should().Be(EntityTarget.Index);
	}

	[Test]
	public void ProductCatalogContextHasWriteTarget()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		ctx.IndexStrategy!.WriteTarget.Should().Be("idx-products");
	}

	[Test]
	public void ProductCatalogContextHasIdDelegate()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		ctx.GetId.Should().NotBeNull("ProductCatalog has [Id] on its Sku property");

		var product = new ProductCatalog { Sku = "SKU-42" };
		ctx.GetId!(product).Should().Be("SKU-42");
	}

	[Test]
	public void ProductCatalogContextHasContentHashDelegate()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		ctx.GetContentHash.Should().NotBeNull("ProductCatalog has [ContentHash] on its ContentHash property");
		ctx.ContentHashFieldName.Should().Be("content_hash");
	}

	[Test]
	public void ProductCatalogMappingsJsonContainsAllFields()
	{
		var json = TestMappingContext.ProductCatalog.Context.GetMappingsJson!();
		using var doc = JsonDocument.Parse(json);
		var props = doc.RootElement.GetProperty("properties");

		props.GetProperty("sku").GetProperty("type").GetString().Should().Be("keyword");
		props.GetProperty("name").GetProperty("type").GetString().Should().Be("text");
		props.GetProperty("name").GetProperty("analyzer").GetString().Should().Be("product_autocomplete");
		props.GetProperty("name").GetProperty("search_analyzer").GetString().Should().Be("standard");
		props.GetProperty("description").GetProperty("type").GetString().Should().Be("text");
		props.GetProperty("category").GetProperty("type").GetString().Should().Be("keyword");
		props.GetProperty("category").GetProperty("normalizer").GetString().Should().Be("lowercase_ascii");
		props.GetProperty("price").GetProperty("type").GetString().Should().Be("double");
		props.GetProperty("tags").GetProperty("type").GetString().Should().Be("keyword");
		props.GetProperty("content_hash").GetProperty("type").GetString().Should().Be("keyword");
		props.GetProperty("updated_at").GetProperty("type").GetString().Should().Be("date");
	}

	[Test]
	public void ProductCatalogSettingsJsonContainsRefreshInterval()
	{
		var json = TestMappingContext.ProductCatalog.Context.GetSettingsJson!();
		using var doc = JsonDocument.Parse(json);
		doc.RootElement.GetProperty("refresh_interval").GetString().Should().Be("1s");
	}

	[Test]
	public void ProductCatalogConfigureAnalysisIsWiredUp()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		ctx.ConfigureAnalysis.Should().NotBeNull(
			"ProductCatalog implements IConfigureElasticsearch with ConfigureAnalysis");
	}

	[Test]
	public void ProductCatalogConfigureAnalysisProducesAutocompleteAnalyzerAndNormalizer()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		var builder = new AnalysisBuilder();
		var result = ctx.ConfigureAnalysis!(builder);
		result.HasConfiguration.Should().BeTrue();

		var settings = result.Build();
		var analysisJson = settings.ToJsonString();
		using var doc = JsonDocument.Parse(analysisJson);

		doc.RootElement.GetProperty("filter").GetProperty("edge_ngram_filter")
			.GetProperty("type").GetString().Should().Be("edge_ngram");

		var analyzer = doc.RootElement.GetProperty("analyzer").GetProperty("product_autocomplete");
		analyzer.GetProperty("type").GetString().Should().Be("custom");
		analyzer.GetProperty("tokenizer").GetString().Should().Be("standard");
		analyzer.GetProperty("filter").EnumerateArray().Select(e => e.GetString())
			.Should().ContainInOrder("lowercase", "edge_ngram_filter");

		var normalizer = doc.RootElement.GetProperty("normalizer").GetProperty("lowercase_ascii");
		normalizer.GetProperty("type").GetString().Should().Be("custom");
		normalizer.GetProperty("filter").EnumerateArray().Select(e => e.GetString())
			.Should().ContainInOrder("lowercase", "asciifolding");
	}

	[Test]
	public void ProductCatalogConfigureMappingsProducesRuntimeField()
	{
		var builder = new ProductCatalogMappingsBuilder();
		var result = ProductCatalog.ConfigureMappings(builder);
		result.HasConfiguration.Should().BeTrue(
			"ConfigureMappings adds a price_tier runtime field");
	}

	// --- ProductCatalogCatalog (Index with Alias) variant ---

	[Test]
	public void ProductCatalogCatalogContextHasAliasConfiguration()
	{
		var ctx = TestMappingContext.ProductCatalogCatalog.Context;
		ctx.EntityTarget.Should().Be(EntityTarget.Index);
		ctx.IndexStrategy!.WriteTarget.Should().Be("cat-products");
		ctx.IndexStrategy.DatePattern.Should().Be("yyyy.MM.dd.HHmmss");
		ctx.SearchStrategy!.ReadAlias.Should().Be("cat-products-search");
		ctx.SearchStrategy.Pattern.Should().Be("cat-products-*");
	}

	[Test]
	public void ProductCatalogCatalogContextSharesMappingsWithBaseVariant()
	{
		var baseJson = TestMappingContext.ProductCatalog.Context.GetMappingsJson!();
		var catalogJson = TestMappingContext.ProductCatalogCatalog.Context.GetMappingsJson!();
		catalogJson.Should().Be(baseJson,
			"both variants of ProductCatalog have the same field attributes");
	}

	[Test]
	public void ProductCatalogCatalogContextHasDifferentHashFromBase()
	{
		var baseHash = TestMappingContext.ProductCatalog.Hash;
		var catalogHash = TestMappingContext.ProductCatalogCatalog.Hash;
		catalogHash.Should().NotBe(baseHash,
			"different entity-level settings produce different hashes");
	}

	// --- HashableArticle (Index) ---

	[Test]
	public void HashableArticleContextHasCorrectEntityTarget()
	{
		var ctx = TestMappingContext.HashableArticle.Context;
		ctx.EntityTarget.Should().Be(EntityTarget.Index);
		ctx.IndexStrategy!.WriteTarget.Should().Be("hashable-articles");
	}

	[Test]
	public void HashableArticleContextHasIdAndContentHashDelegates()
	{
		var ctx = TestMappingContext.HashableArticle.Context;
		ctx.GetId.Should().NotBeNull("HashableArticle has [Id] on its Id property");
		ctx.GetContentHash.Should().NotBeNull("HashableArticle has [ContentHash] on its Hash property");
		ctx.ContentHashFieldName.Should().Be("hash");

		var article = new HashableArticle { Id = "art-1", Hash = "abc123" };
		ctx.GetId!(article).Should().Be("art-1");
		ctx.GetContentHash!(article).Should().Be("abc123");
	}

	[Test]
	public void HashableArticleMappingsJsonContainsAllFields()
	{
		var json = TestMappingContext.HashableArticle.Context.GetMappingsJson!();
		using var doc = JsonDocument.Parse(json);
		var props = doc.RootElement.GetProperty("properties");

		props.GetProperty("id").GetProperty("type").GetString().Should().Be("keyword");
		props.GetProperty("title").GetProperty("type").GetString().Should().Be("text");
		props.GetProperty("title").GetProperty("analyzer").GetString().Should().Be("html_content");
		props.GetProperty("hash").GetProperty("type").GetString().Should().Be("keyword");
		props.GetProperty("index_batch_date").GetProperty("type").GetString().Should().Be("date");
		props.GetProperty("last_updated").GetProperty("type").GetString().Should().Be("date");
	}

	[Test]
	public void HashableArticleConfigureAnalysisProducesHtmlContentAnalyzer()
	{
		var ctx = TestMappingContext.HashableArticle.Context;
		ctx.ConfigureAnalysis.Should().NotBeNull();

		var builder = new AnalysisBuilder();
		var result = ctx.ConfigureAnalysis!(builder);
		result.HasConfiguration.Should().BeTrue();

		var settings = result.Build();
		var analysisJson = settings.ToJsonString();
		using var doc = JsonDocument.Parse(analysisJson);

		doc.RootElement.GetProperty("char_filter").GetProperty("html_stripper")
			.GetProperty("type").GetString().Should().Be("html_strip");

		var analyzer = doc.RootElement.GetProperty("analyzer").GetProperty("html_content");
		analyzer.GetProperty("type").GetString().Should().Be("custom");
		analyzer.GetProperty("tokenizer").GetString().Should().Be("standard");
		analyzer.GetProperty("char_filter").EnumerateArray().Select(e => e.GetString())
			.Should().Contain("html_stripper");
		analyzer.GetProperty("filter").EnumerateArray().Select(e => e.GetString())
			.Should().ContainInOrder("lowercase", "asciifolding");
	}

	// --- SemanticArticle (Index) ---

	[Test]
	public void SemanticArticleContextHasCorrectEntityTarget()
	{
		var ctx = TestMappingContext.SemanticArticle.Context;
		ctx.EntityTarget.Should().Be(EntityTarget.Index);
		ctx.IndexStrategy!.WriteTarget.Should().Be("semantic-articles");
	}

	[Test]
	public void SemanticArticleMappingsJsonContainsSemanticTextField()
	{
		var json = TestMappingContext.SemanticArticle.Context.GetMappingsJson!();
		using var doc = JsonDocument.Parse(json);
		var props = doc.RootElement.GetProperty("properties");

		props.GetProperty("id").GetProperty("type").GetString().Should().Be("keyword");
		props.GetProperty("title").GetProperty("type").GetString().Should().Be("text");
		props.GetProperty("title").GetProperty("analyzer").GetString().Should().Be("semantic_content");
		props.GetProperty("semantic_text").GetProperty("type").GetString().Should().Be("semantic_text");
		props.GetProperty("semantic_text").GetProperty("inference_id").GetString().Should().Be("test-elser-inference");
		props.GetProperty("created").GetProperty("type").GetString().Should().Be("date");
	}

	[Test]
	public void SemanticArticleConfigureAnalysisProducesSemanticContentAnalyzer()
	{
		var ctx = TestMappingContext.SemanticArticle.Context;
		ctx.ConfigureAnalysis.Should().NotBeNull();

		var builder = new AnalysisBuilder();
		var result = ctx.ConfigureAnalysis!(builder);
		result.HasConfiguration.Should().BeTrue();

		var settings = result.Build();
		var analysisJson = settings.ToJsonString();
		using var doc = JsonDocument.Parse(analysisJson);

		var analyzer = doc.RootElement.GetProperty("analyzer").GetProperty("semantic_content");
		analyzer.GetProperty("type").GetString().Should().Be("custom");
		analyzer.GetProperty("tokenizer").GetString().Should().Be("standard");
		analyzer.GetProperty("filter").EnumerateArray().Select(e => e.GetString())
			.Should().ContainInOrder("lowercase", "asciifolding");
	}

	// --- Hash stability ---

	[Test]
	public void HashesAreStableAcrossMultipleAccesses()
	{
		var hash1 = TestMappingContext.ServerMetricsEvent.Hash;
		var hash2 = TestMappingContext.ServerMetricsEvent.Hash;
		hash1.Should().Be(hash2);

		var hash3 = TestMappingContext.ProductCatalog.Hash;
		var hash4 = TestMappingContext.ProductCatalog.Hash;
		hash3.Should().Be(hash4);
	}

	[Test]
	public void AllContextsAreRegistered()
	{
		TestMappingContext.All.Should().HaveCount(5);
		TestMappingContext.All.Should().Contain(TestMappingContext.ServerMetricsEvent.Context);
		TestMappingContext.All.Should().Contain(TestMappingContext.ProductCatalog.Context);
		TestMappingContext.All.Should().Contain(TestMappingContext.ProductCatalogCatalog.Context);
		TestMappingContext.All.Should().Contain(TestMappingContext.HashableArticle.Context);
		TestMappingContext.All.Should().Contain(TestMappingContext.SemanticArticle.Context);
	}
}
