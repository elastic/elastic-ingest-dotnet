// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Ingest.Elasticsearch.Strategies;
using Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;
using Elastic.Mapping;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Strategies;

/*
 * Tests: IngestStrategies factory composition from ElasticsearchTypeContext
 *
 * No Elasticsearch cluster required — verifies that IngestStrategies.ForContext<>(),
 * IngestStrategies.DataStream<>(), and IngestStrategies.Index<>() correctly compose:
 *
 *   IIngestStrategy<TEvent>
 *   ├── IDocumentIngestStrategy     (DataStreamIngestStrategy / TypeContextIndexIngestStrategy)
 *   ├── IBootstrapStrategy          (DefaultBootstrapStrategy with step pipeline)
 *   │   ├── ComponentTemplateStep
 *   │   ├── DataStreamTemplateStep | IndexTemplateStep
 *   │   ├── DataStreamLifecycleStep (optional, when retention specified)
 *   │   └── IlmPolicyStep           (optional, when ILM policy specified)
 *   ├── IIndexProvisioningStrategy  (AlwaysCreateProvisioning / HashBasedReuseProvisioning)
 *   ├── IAliasStrategy              (NoAliasStrategy / LatestAndSearchAliasStrategy)
 *   └── Delegates
 *       ├── GetMappingsJson         → from ElasticsearchTypeContext
 *       ├── GetMappingSettings      → merged entity settings + ConfigureAnalysis output
 *       └── DataStreamType          → from IndexStrategy.Type
 */
public class CompositeStrategyTests
{
	// --- ForContext auto-detection ---

	[Test]
	public void ForContextServerMetricsEventSelectsDataStreamStrategy()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var strategy = IngestStrategies.ForContext<ServerMetricsEvent>(ctx);

		strategy.DocumentIngest.Should().BeOfType<DataStreamIngestStrategy<ServerMetricsEvent>>();
		strategy.Bootstrap.Should().BeOfType<DefaultBootstrapStrategy>();
		strategy.Provisioning.Should().BeOfType<AlwaysCreateProvisioning>();
		strategy.AliasStrategy.Should().BeOfType<NoAliasStrategy>();
	}

	[Test]
	public void ForContextProductCatalogSelectsIndexStrategy()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		var strategy = IngestStrategies.ForContext<ProductCatalog>(ctx);

		strategy.DocumentIngest.Should().BeOfType<TypeContextIndexIngestStrategy<ProductCatalog>>();
		strategy.Bootstrap.Should().BeOfType<DefaultBootstrapStrategy>();
		strategy.AliasStrategy.Should().BeOfType<NoAliasStrategy>();
		strategy.Provisioning.Should().BeOfType<HashBasedReuseProvisioning>(
			"ProductCatalog has [ContentHash] which triggers hash-based reuse");
	}

	[Test]
	public void ForContextProductCatalogCatalogSelectsIndexWithAlias()
	{
		var ctx = TestMappingContext.ProductCatalogCatalog.Context;
		var strategy = IngestStrategies.ForContext<ProductCatalog>(ctx);

		strategy.DocumentIngest.Should().BeOfType<TypeContextIndexIngestStrategy<ProductCatalog>>();
		strategy.AliasStrategy.Should().BeOfType<LatestAndSearchAliasStrategy>(
			"catalog variant has ReadAlias configured");
		strategy.Provisioning.Should().BeOfType<HashBasedReuseProvisioning>();
	}

	[Test]
	public void ForContextHashableArticleSelectsIndexWithHashReuse()
	{
		var ctx = TestMappingContext.HashableArticle.Context;
		var strategy = IngestStrategies.ForContext<HashableArticle>(ctx);

		strategy.DocumentIngest.Should().BeOfType<TypeContextIndexIngestStrategy<HashableArticle>>();
		strategy.Provisioning.Should().BeOfType<HashBasedReuseProvisioning>(
			"HashableArticle has [ContentHash] which triggers hash-based reuse");
		strategy.AliasStrategy.Should().BeOfType<NoAliasStrategy>();
	}

	[Test]
	public void ForContextSemanticArticleSelectsIndexStrategy()
	{
		var ctx = TestMappingContext.SemanticArticle.Context;
		var strategy = IngestStrategies.ForContext<SemanticArticle>(ctx);

		strategy.DocumentIngest.Should().BeOfType<TypeContextIndexIngestStrategy<SemanticArticle>>();
		strategy.AliasStrategy.Should().BeOfType<NoAliasStrategy>();
		strategy.Provisioning.Should().BeOfType<AlwaysCreateProvisioning>(
			"SemanticArticle has no [ContentHash]");
	}

	// --- Explicit strategy factories ---

	[Test]
	public void DataStreamFactoryResolvesCorrectTemplateName()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var strategy = IngestStrategies.DataStream<ServerMetricsEvent>(ctx);

		strategy.TemplateName.Should().Be("logs-srvmetrics");
		strategy.TemplateWildcard.Should().Be("logs-srvmetrics-*");
	}

	[Test]
	public void DataStreamFactoryWithRetentionHasLifecycleStep()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var strategy = IngestStrategies.DataStream<ServerMetricsEvent>(ctx, "30d");

		var bootstrap = strategy.Bootstrap.Should().BeOfType<DefaultBootstrapStrategy>().Subject;
		bootstrap.Steps.Should().Contain(s => s is DataStreamLifecycleStep);
	}

	[Test]
	public void DataStreamFactoryDefaultBootstrapHasComponentAndDataStreamTemplateSteps()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var strategy = IngestStrategies.DataStream<ServerMetricsEvent>(ctx);

		var bootstrap = strategy.Bootstrap.Should().BeOfType<DefaultBootstrapStrategy>().Subject;
		bootstrap.Steps.Should().HaveCount(2);
		bootstrap.Steps[0].Should().BeOfType<ComponentTemplateStep>();
		bootstrap.Steps[1].Should().BeOfType<DataStreamTemplateStep>();
	}

	[Test]
	public void DataStreamFactoryWithCustomBootstrapUsesProvidedBootstrap()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var custom = BootstrapStrategies.DataStreamWithIlm("my-policy");
		var strategy = IngestStrategies.DataStream<ServerMetricsEvent>(ctx, custom);

		strategy.Bootstrap.Should().BeSameAs(custom);
	}

	[Test]
	public void IndexFactoryResolvesCorrectTemplateName()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		var strategy = IngestStrategies.Index<ProductCatalog>(ctx);

		strategy.TemplateName.Should().Be("idx-products-template");
		strategy.TemplateWildcard.Should().Be("idx-products*",
			"fixed-name index (no DatePattern) uses trailing wildcard that matches the exact name");
	}

	[Test]
	public void IndexFactoryDefaultBootstrapHasComponentAndIndexTemplateSteps()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		var strategy = IngestStrategies.Index<ProductCatalog>(ctx);

		var bootstrap = strategy.Bootstrap.Should().BeOfType<DefaultBootstrapStrategy>().Subject;
		bootstrap.Steps.Should().HaveCount(2);
		bootstrap.Steps[0].Should().BeOfType<ComponentTemplateStep>();
		bootstrap.Steps[1].Should().BeOfType<IndexTemplateStep>();
	}

	[Test]
	public void IndexFactoryWithIlmBootstrapHasIlmStep()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		var custom = BootstrapStrategies.IndexWithIlm("7-day-policy");
		var strategy = IngestStrategies.Index<ProductCatalog>(ctx, custom);

		var bootstrap = strategy.Bootstrap.Should().BeOfType<DefaultBootstrapStrategy>().Subject;
		bootstrap.Steps.Should().Contain(s => s is IlmPolicyStep);
		bootstrap.Steps.Should().Contain(s => s is ComponentTemplateStep);
		bootstrap.Steps.Should().Contain(s => s is IndexTemplateStep);
	}

	// --- Catalog variant ---

	[Test]
	public void CatalogVariantResolvesCorrectTemplateName()
	{
		var ctx = TestMappingContext.ProductCatalogCatalog.Context;
		var strategy = IngestStrategies.Index<ProductCatalog>(ctx);

		strategy.TemplateName.Should().Be("cat-products-template");
		strategy.TemplateWildcard.Should().Be("cat-products-*");
	}

	// --- GetMappingsJson propagation ---

	[Test]
	public void StrategyPropagatesGetMappingsJsonFromContext()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		var strategy = IngestStrategies.Index<ProductCatalog>(ctx);

		strategy.GetMappingsJson.Should().NotBeNull();
		strategy.GetMappingsJson!().Should().Contain("\"sku\"");
		strategy.GetMappingsJson!().Should().Contain("\"product_autocomplete\"");
	}

	[Test]
	public void StrategyPropagatesGetMappingSettingsWithAnalysisAndNormalizer()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		var strategy = IngestStrategies.Index<ProductCatalog>(ctx);

		strategy.GetMappingSettings.Should().NotBeNull();
		var settings = strategy.GetMappingSettings!();
		settings.Should().Contain("\"analysis\"",
			"merged settings should include analysis from ConfigureAnalysis");
		settings.Should().Contain("\"normalizer\"",
			"merged settings should include normalizer from ConfigureAnalysis");
		settings.Should().Contain("\"lowercase_ascii\"");
	}

	[Test]
	public void HashableArticleStrategyPropagatesHtmlContentAnalysis()
	{
		var ctx = TestMappingContext.HashableArticle.Context;
		var strategy = IngestStrategies.Index<HashableArticle>(ctx);

		strategy.GetMappingsJson.Should().NotBeNull();
		strategy.GetMappingsJson!().Should().Contain("\"html_content\"");

		strategy.GetMappingSettings.Should().NotBeNull();
		var settings = strategy.GetMappingSettings!();
		settings.Should().Contain("\"html_content\"");
		settings.Should().Contain("\"html_stripper\"");
	}

	[Test]
	public void SemanticArticleStrategyPropagatesSemanticContentAnalysis()
	{
		var ctx = TestMappingContext.SemanticArticle.Context;
		var strategy = IngestStrategies.Index<SemanticArticle>(ctx);

		strategy.GetMappingsJson.Should().NotBeNull();
		strategy.GetMappingsJson!().Should().Contain("\"semantic_content\"");
		strategy.GetMappingsJson!().Should().Contain("\"semantic_text\"");
		strategy.GetMappingsJson!().Should().Contain("\"test-elser-inference\"");

		strategy.GetMappingSettings.Should().NotBeNull();
		strategy.GetMappingSettings!().Should().Contain("\"semantic_content\"");
	}

	[Test]
	public void DataStreamStrategyPropagatesDataStreamType()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var strategy = IngestStrategies.DataStream<ServerMetricsEvent>(ctx);

		strategy.DataStreamType.Should().Be("logs");
	}

	// --- V2 variants resolve to same template names/wildcards ---

	[Test]
	public void V2IndexResolvesToSameTemplateNameAsV1()
	{
		var v1 = IngestStrategies.Index<ProductCatalog>(TestMappingContext.ProductCatalog.Context);
		var v2 = IngestStrategies.Index<ProductCatalogV2>(TestMappingContext.ProductCatalogV2.Context);

		v2.TemplateName.Should().Be(v1.TemplateName,
			"V2 targets the same index so template name must match");
		v2.TemplateWildcard.Should().Be(v1.TemplateWildcard);
	}

	[Test]
	public void V2DataStreamResolvesToSameTemplateNameAsV1()
	{
		var v1 = IngestStrategies.DataStream<ServerMetricsEvent>(TestMappingContext.ServerMetricsEvent.Context);
		var v2 = IngestStrategies.DataStream<ServerMetricsEventV2>(TestMappingContext.ServerMetricsEventV2.Context);

		v2.TemplateName.Should().Be(v1.TemplateName,
			"V2 targets the same data stream so template name must match");
		v2.TemplateWildcard.Should().Be(v1.TemplateWildcard);
	}

	[Test]
	public void V2CatalogResolvesToSameTemplateNameAsV1Catalog()
	{
		var v1 = IngestStrategies.Index<ProductCatalog>(TestMappingContext.ProductCatalogCatalog.Context);
		var v2 = IngestStrategies.Index<ProductCatalogV2>(TestMappingContext.ProductCatalogV2Catalog.Context);

		v2.TemplateName.Should().Be(v1.TemplateName);
		v2.TemplateWildcard.Should().Be(v1.TemplateWildcard);
	}

	[Test]
	public void V2SettingsJsonDiffersFromV1()
	{
		var v1Settings = IngestStrategies.Index<ProductCatalog>(TestMappingContext.ProductCatalog.Context)
			.GetMappingSettings!();
		var v2Settings = IngestStrategies.Index<ProductCatalogV2>(TestMappingContext.ProductCatalogV2.Context)
			.GetMappingSettings!();

		v1Settings.Should().NotBe(v2Settings,
			"V2 analysis has different edge_ngram params and stop filter");

		v2Settings.Should().Contain("\"stop\"",
			"V2 adds stop filter to product_autocomplete analyzer");
	}
}
