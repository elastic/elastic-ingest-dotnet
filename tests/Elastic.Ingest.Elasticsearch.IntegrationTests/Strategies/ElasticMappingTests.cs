// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Linq;
using System.Text.Json;
using Elastic.Ingest.Elasticsearch.Strategies;
using Elastic.Mapping;
using Elastic.Mapping.Analysis;
using FluentAssertions;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Strategies;

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
 *   │   ├── GetSettingsJson  → { refresh_interval: "5s" }
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
		var result = ServerMetricsEventConfig.ConfigureMappings(builder);
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
		doc.RootElement.GetProperty("refresh_interval").GetString().Should().Be("5s");
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
		var result = ProductCatalogConfig.ConfigureMappings(builder);
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
		// All is now keyed by Type — variants of the same type share a single entry
		TestMappingContext.All.Should().ContainKey(typeof(ServerMetricsEvent));
		TestMappingContext.All.Should().ContainKey(typeof(ServerMetricsEventV2));
		TestMappingContext.All.Should().ContainKey(typeof(ProductCatalog));
		TestMappingContext.All.Should().ContainKey(typeof(ProductCatalogV2));
		TestMappingContext.All.Should().ContainKey(typeof(HashableArticle));
		TestMappingContext.All.Should().ContainKey(typeof(SemanticArticle));
	}

	// --- V2 variants (mapping evolution) ---

	[Test]
	public void V2ServerMetricsEventMappingContextHashDiffersFromV1()
	{
		var v1Hash = TestMappingContext.ServerMetricsEvent.Hash;
		var v2Hash = TestMappingContext.ServerMetricsEventV2.Hash;
		v2Hash.Should().NotBe(v1Hash,
			"V2 subclass adds error_code field → different mapping-context hash");
	}

	[Test]
	public void V2ProductCatalogMappingContextHashDiffersFromV1()
	{
		var v1Hash = TestMappingContext.ProductCatalog.Hash;
		var v2Hash = TestMappingContext.ProductCatalogV2.Hash;
		v2Hash.Should().NotBe(v1Hash,
			"V2 subclass adds is_featured field → different mapping-context hash");
	}

	[Test]
	public void V2ServerMetricsEventSettingsJsonDiffersFromV1()
	{
		var v1Settings = IngestStrategies.DataStream<ServerMetricsEvent>(
			TestMappingContext.ServerMetricsEvent.Context).GetMappingSettings!();
		var v2Settings = IngestStrategies.DataStream<ServerMetricsEventV2>(
			TestMappingContext.ServerMetricsEventV2.Context).GetMappingSettings!();

		v2Settings.Should().NotBe(v1Settings,
			"V2 adds stop filter to log_message analyzer → different merged settings JSON");
		v2Settings.Should().Contain("\"stop\"");
	}

	[Test]
	public void V2ProductCatalogSettingsJsonDiffersFromV1()
	{
		var v1Settings = IngestStrategies.Index<ProductCatalog>(
			TestMappingContext.ProductCatalog.Context).GetMappingSettings!();
		var v2Settings = IngestStrategies.Index<ProductCatalogV2>(
			TestMappingContext.ProductCatalogV2.Context).GetMappingSettings!();

		v2Settings.Should().NotBe(v1Settings,
			"V2 has different edge_ngram params + stop filter → different merged settings JSON");
		v2Settings.Should().Contain("\"stop\"");
	}

	[Test]
	public void V2CatalogSettingsJsonDiffersFromV1Catalog()
	{
		var v1Settings = IngestStrategies.Index<ProductCatalog>(
			TestMappingContext.ProductCatalogCatalog.Context).GetMappingSettings!();
		var v2Settings = IngestStrategies.Index<ProductCatalogV2>(
			TestMappingContext.ProductCatalogV2Catalog.Context).GetMappingSettings!();

		v2Settings.Should().NotBe(v1Settings,
			"V2 catalog variant has different analysis → different merged settings JSON");
	}

	[Test]
	public void V2VariantsShareSameEntityTarget()
	{
		TestMappingContext.ServerMetricsEventV2.Context.EntityTarget
			.Should().Be(TestMappingContext.ServerMetricsEvent.Context.EntityTarget);
		TestMappingContext.ProductCatalogV2.Context.EntityTarget
			.Should().Be(TestMappingContext.ProductCatalog.Context.EntityTarget);
		TestMappingContext.ProductCatalogV2Catalog.Context.EntityTarget
			.Should().Be(TestMappingContext.ProductCatalogCatalog.Context.EntityTarget);
	}

	[Test]
	public void V2VariantsShareSameWriteTarget()
	{
		TestMappingContext.ProductCatalogV2.Context.IndexStrategy!.WriteTarget
			.Should().Be(TestMappingContext.ProductCatalog.Context.IndexStrategy!.WriteTarget,
				"V2 and V1 map to the same Elasticsearch index name");

		TestMappingContext.ProductCatalogV2Catalog.Context.IndexStrategy!.WriteTarget
			.Should().Be(TestMappingContext.ProductCatalogCatalog.Context.IndexStrategy!.WriteTarget,
				"V2 and V1 catalog map to the same Elasticsearch prefix");
	}

	[Test]
	public void V2ServerMetricsEventSharesSameDataStreamName()
	{
		TestMappingContext.ServerMetricsEventV2.Context.IndexStrategy!.DataStreamName
			.Should().Be(TestMappingContext.ServerMetricsEvent.Context.IndexStrategy!.DataStreamName,
				"V2 and V1 map to the same data stream");
	}

	[Test]
	public void V2ServerMetricsEventConfigureAnalysisProducesStopFilter()
	{
		var ctx = TestMappingContext.ServerMetricsEventV2.Context;
		var builder = new AnalysisBuilder();
		var result = ctx.ConfigureAnalysis!(builder);
		result.HasConfiguration.Should().BeTrue();

		var settings = result.Build();
		var analysisJson = settings.ToJsonString();
		using var doc = JsonDocument.Parse(analysisJson);
		var analyzer = doc.RootElement.GetProperty("analyzer").GetProperty("log_message");
		analyzer.GetProperty("filter").EnumerateArray().Select(e => e.GetString())
			.Should().Contain("stop", "V2 log_message analyzer adds a stop filter");
	}

	[Test]
	public void V2ProductCatalogConfigureAnalysisHasWidenedEdgeNgram()
	{
		var ctx = TestMappingContext.ProductCatalogV2.Context;
		var builder = new AnalysisBuilder();
		var result = ctx.ConfigureAnalysis!(builder);
		result.HasConfiguration.Should().BeTrue();

		var settings = result.Build();
		var analysisJson = settings.ToJsonString();
		using var doc = JsonDocument.Parse(analysisJson);
		var filter = doc.RootElement.GetProperty("filter").GetProperty("edge_ngram_filter");
		filter.GetProperty("min_gram").GetInt32().Should().Be(3, "V2 widens min_gram to 3");
		filter.GetProperty("max_gram").GetInt32().Should().Be(20, "V2 widens max_gram to 20");
	}
}
