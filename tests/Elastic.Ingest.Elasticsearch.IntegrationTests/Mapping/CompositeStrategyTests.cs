// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Ingest.Elasticsearch.Strategies;
using Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;
using Elastic.Mapping;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Mapping;

/// <summary>
/// Verifies that the IngestStrategies factory correctly composes strategies
/// from realistic source-generated <see cref="ElasticsearchTypeContext"/> instances.
/// These tests do not require a running Elasticsearch cluster.
/// </summary>
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
		strategy.TemplateWildcard.Should().Be("idx-products-*");
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
	public void StrategyPropagatesGetMappingSettingsFromContext()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		var strategy = IngestStrategies.Index<ProductCatalog>(ctx);

		strategy.GetMappingSettings.Should().NotBeNull();
		strategy.GetMappingSettings!().Should().Contain("\"analysis\"",
			"merged settings should include analysis from ConfigureAnalysis");
	}

	[Test]
	public void DataStreamStrategyPropagatesDataStreamType()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var strategy = IngestStrategies.DataStream<ServerMetricsEvent>(ctx);

		strategy.DataStreamType.Should().Be("logs");
	}
}
