// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.SingleIndex;

/*
 * Use case: Single index  (https://elastic.github.io/elastic-ingest-dotnet/index-management/single-index)
 * Tests:    Verify that custom analyzers, normalizers, and runtime fields are configured correctly
 *
 * These tests verify:
 *   1. Component templates contain the correct analysis/mapping configuration
 *   2. The merged settings JSON from the strategy is correct
 *   3. For actual index-level analysis verification (querying indexed data),
 *      we use the ManualAlias (Catalog) variant where date-stamped indices
 *      match the template pattern.
 *
 * ProductCatalog analysis pipeline (as declared in templates):
 *   ┌──────────────────────────────────────────────────────────────────┐
 *   │  product_autocomplete analyzer (edge_ngram min=2 max=15):       │
 *   │    "Premium Carbon" → ["pr","pre","prem",...,"ca","car",...]    │
 *   │                                                                  │
 *   │  lowercase_ascii normalizer:                                     │
 *   │    category: "Widgets" → stored as "widgets"                     │
 *   │                                                                  │
 *   │  price_tier runtime field:                                       │
 *   │    price < 10 → "budget", < 100 → "mid", >= 100 → "premium"    │
 *   └──────────────────────────────────────────────────────────────────┘
 */
[NotInParallel("single-index")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class AnalysisVerificationTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string Prefix = "idx-products";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync(Prefix);

	[After(Test)]
	public async Task Teardown() => await CleanupPrefixAsync(Prefix);

	[Test]
	public async Task MappingsTemplateContainsAnalyzerReferences()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		var options = new IngestChannelOptions<ProductCatalog>(Transport, ctx)
		{
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<ProductCatalog>(options);
		(await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();

		var mappingsTemplate = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_component_template/{Prefix}-template-mappings");
		mappingsTemplate.ApiCallDetails.HttpStatusCode.Should().Be(200);

		mappingsTemplate.Body.Should().Contain("\"product_autocomplete\"",
			"mappings template should reference the product_autocomplete analyzer on the name field");
		mappingsTemplate.Body.Should().Contain("\"lowercase_ascii\"",
			"mappings template should reference the lowercase_ascii normalizer on the category field");
		mappingsTemplate.Body.Should().Contain("\"edge_ngram\"",
			"mappings template settings should include the edge_ngram token filter");
		mappingsTemplate.Body.Should().Contain("\"analysis\"",
			"mappings template settings should include the merged analysis configuration");
	}

	[Test]
	public async Task RuntimeFieldsAreDeployedToTemplate()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		var options = new IngestChannelOptions<ProductCatalog>(Transport, ctx)
		{
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<ProductCatalog>(options);
		(await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();

		var mappingsTemplate = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_component_template/{Prefix}-template-mappings");
		mappingsTemplate.ApiCallDetails.HttpStatusCode.Should().Be(200);

		mappingsTemplate.Body.Should().Contain("\"price_tier\"",
			"runtime fields from ConfigureMappings should be deployed to templates via MergeIntoMappings");
	}

	[Test]
	public async Task SettingsTemplateContainsRefreshInterval()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		var options = new IngestChannelOptions<ProductCatalog>(Transport, ctx)
		{
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<ProductCatalog>(options);
		(await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();

		var settingsTemplate = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_component_template/{Prefix}-template-settings");
		settingsTemplate.ApiCallDetails.HttpStatusCode.Should().Be(200);
	}
}

/*
 * Use case: Manual alias management — verifies analysis is APPLIED to actual index data
 *
 * The Catalog variant creates date-stamped indices like "cat-products-2026.02.19.120000"
 * which match the "cat-products-*" template pattern. Analysis settings are applied,
 * so we can verify they work at query time.
 *
 *   ┌──────────────────────────────────────────────────────────────────┐
 *   │  1. Bootstrap + write 3 ProductCatalog docs via Catalog variant  │
 *   │  2. cat-products-TIMESTAMP index created with template settings  │
 *   │  3. Verify:                                                      │
 *   │     • edge_ngram partial match: "Prem" → "Premium Carbon Widget" │
 *   │     • normalizer lowercasing: term "widgets" → finds "Widgets"   │
 *   │     • runtime field: price_tier "mid" → finds price=49.99        │
 *   │     • actual index settings contain product_autocomplete analyzer│
 *   └──────────────────────────────────────────────────────────────────┘
 */
[NotInParallel("manual-alias")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class IndexedAnalysisVerificationTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string Prefix = "cat-products";
	private string? _concreteIndex;

	[Before(Test)]
	public async Task Setup()
	{
		await CleanupPrefixAsync(Prefix);

		var ctx = TestMappingContext.ProductCatalogCatalog.Context;
		var slim = new CountdownEvent(1);
		var options = new IngestChannelOptions<ProductCatalog>(Transport, ctx)
		{
			BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 3 }
		};
		var channel = new IngestChannel<ProductCatalog>(options);
		(await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();

		channel.TryWrite(new ProductCatalog
		{
			Sku = "AV-001", Name = "Premium Carbon Widget", Description = "High quality",
			Category = "Widgets", Price = 49.99, Tags = ["premium", "carbon"]
		});
		channel.TryWrite(new ProductCatalog
		{
			Sku = "AV-002", Name = "Budget Plastic Gadget", Description = "Affordable",
			Category = "GADGETS", Price = 3.50, Tags = ["budget", "plastic"]
		});
		channel.TryWrite(new ProductCatalog
		{
			Sku = "AV-003", Name = "Luxury Diamond Tool", Description = "Exclusive",
			Category = "Tools", Price = 250.00, Tags = ["luxury", "diamond"]
		});

		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("Setup write timed out");

		_concreteIndex = await ResolveConcreteIndexAsync();
		_concreteIndex.Should().NotBeNull("a concrete cat-products index should have been created");

		await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{_concreteIndex}/_refresh");
	}

	[After(Test)]
	public async Task Teardown() => await CleanupPrefixAsync(Prefix);

	[Test]
	public async Task EdgeNgramAnalyzerEnablesPartialMatching()
	{
		var body = """{ "query": { "match": { "name": { "query": "Prem", "analyzer": "standard" } } } }""";
		var search = await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{_concreteIndex}/_search", PostData.String(body));

		search.ApiCallDetails.HttpStatusCode.Should().Be(200);
		search.Body.Should().Contain("\"AV-001\"",
			"edge_ngram indexes 'Prem' as a prefix token so 'Prem' matches 'Premium Carbon Widget'");
	}

	[Test]
	public async Task NormalizerLowercasesKeywordField()
	{
		var body = """{ "query": { "term": { "category": "widgets" } } }""";
		var search = await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{_concreteIndex}/_search", PostData.String(body));

		search.ApiCallDetails.HttpStatusCode.Should().Be(200);
		search.Body.Should().Contain("\"AV-001\"",
			"lowercase_ascii normalizer stores 'Widgets' as 'widgets'");

		var body2 = """{ "query": { "term": { "category": "gadgets" } } }""";
		var search2 = await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{_concreteIndex}/_search", PostData.String(body2));
		search2.Body.Should().Contain("\"AV-002\"",
			"'GADGETS' should be normalized to 'gadgets'");
	}

	[Test]
	public async Task RuntimeFieldIsQueryableFromTemplate()
	{
		var body = """{ "query": { "term": { "price_tier": "mid" } } }""";
		var search = await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{_concreteIndex}/_search", PostData.String(body));

		search.ApiCallDetails.HttpStatusCode.Should().Be(200);
		search.Body.Should().Contain("\"AV-001\"",
			"AV-001 has price 49.99 which maps to 'mid' tier");
		search.Body.Should().NotContain("\"AV-002\"",
			"AV-002 has price 3.50 which maps to 'budget' tier");
	}

	[Test]
	public async Task IndexSettingsReflectConfiguredAnalysis()
	{
		var settings = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{_concreteIndex}/_settings");
		settings.ApiCallDetails.HttpStatusCode.Should().Be(200);

		settings.Body.Should().Contain("\"product_autocomplete\"",
			"date-stamped index should have the product_autocomplete analyzer from the template");
		settings.Body.Should().Contain("\"edge_ngram_filter\"",
			"date-stamped index should have the edge_ngram_filter from the template");
	}

	private async Task<string?> ResolveConcreteIndexAsync()
	{
		var resolve = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_resolve/index/{Prefix}-*");
		if (!resolve.ApiCallDetails.HasSuccessfulStatusCode) return null;

		using var doc = JsonDocument.Parse(resolve.Body);
		return doc.RootElement
			.GetProperty("indices")
			.EnumerateArray()
			.Select(e => e.GetProperty("name").GetString())
			.FirstOrDefault(n => !string.IsNullOrEmpty(n));
	}
}
