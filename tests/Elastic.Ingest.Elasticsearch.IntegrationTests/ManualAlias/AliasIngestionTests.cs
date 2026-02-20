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

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.ManualAlias;

/*
 * Use case: Manual alias management  (https://elastic.github.io/elastic-ingest-dotnet/index-management/rollover/manual-alias)
 * Tests:    End-to-end document ingestion into date-rolling aliased indices
 *
 * Document: ProductCatalog (Elastic.Mapping)
 *   Entity: Index  Name="cat-products"  Variant="Catalog"
 *           WriteAlias="cat-products"  ReadAlias="cat-products-search"
 *           SearchPattern="cat-products-*"  DatePattern="yyyy.MM.dd.HHmmss"
 *
 *   ┌───────────────────────────────────────────────────────────────────┐
 *   │  IngestChannel<ProductCatalog>                                    │
 *   │  ├── Bootstrap templates (cat-products-template)                  │
 *   │  ├── TryWrite(product) ─→ _bulk to cat-products-YYYY.MM.DD.HH.. │
 *   │  ├── _resolve/index/cat-products-* → concrete index name          │
 *   │  ├── ApplyAliasesAsync(concreteIndex)                             │
 *   │  │   ├── cat-products-latest  ──→  concrete index                 │
 *   │  │   └── cat-products-search  ──→  concrete index                 │
 *   │  └── Verify via _search on cat-products-search                    │
 *   └───────────────────────────────────────────────────────────────────┘
 *
 * Mapping update mechanism:
 *   New date-stamped indices automatically pick up updated templates.
 *   See MappingEvolutionTests for this scenario.
 *
 * Provisioning: HashBasedReuseProvisioning (ProductCatalog has [ContentHash])
 * Alias:        LatestAndSearchAliasStrategy (ReadAlias configured)
 */
[NotInParallel("manual-alias")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class AliasIngestionTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string Prefix = "cat-products";
	private const string LatestAlias = "cat-products-latest";
	private const string ReadAlias = "cat-products-search";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync(Prefix);

	[After(Test)]
	public async Task Teardown() => await CleanupPrefixAsync(Prefix);

	[Test]
	public async Task EnsureDocumentsIngestViaAliasedCatalog()
	{
		var ctx = TestMappingContext.ProductCatalogCatalog.Context;
		var slim = new CountdownEvent(1);
		var options = new IngestChannelOptions<ProductCatalog>(Transport, ctx)
		{
			BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<ProductCatalog>(options);

		var bootstrapped = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		bootstrapped.Should().BeTrue();

		channel.TryWrite(new ProductCatalog
		{
			Sku = "CAT-001",
			Name = "Catalog Widget",
			Description = "A catalog-managed product.",
			Category = "widgets",
			Price = 12.50,
			Tags = ["catalog"]
		});

		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"Document was not persisted within 10 seconds: {channel}");

		// Resolve the concrete index name
		var resolveResponse = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_resolve/index/{Prefix}-*");
		resolveResponse.ApiCallDetails.HttpStatusCode.Should().Be(200);

		using var resolvedDoc = JsonDocument.Parse(resolveResponse.Body);
		var concreteIndex = resolvedDoc.RootElement
			.GetProperty("indices")
			.EnumerateArray()
			.Select(e => e.GetProperty("name").GetString())
			.FirstOrDefault(n => !string.IsNullOrEmpty(n));
		concreteIndex.Should().NotBeNull("a concrete cat-products index should have been created");

		// Refresh the concrete index
		var refresh = await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{concreteIndex}/_refresh");
		refresh.ApiCallDetails.HasSuccessfulStatusCode.Should().BeTrue();

		// Search to verify the document landed
		var searchViaPattern = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{concreteIndex}/_search");
		searchViaPattern.ApiCallDetails.HttpStatusCode.Should().Be(200);
		searchViaPattern.Body.Should().Contain("\"CAT-001\"");

		// Apply aliases via the channel using the concrete index name
		await channel.ApplyAliasesAsync(concreteIndex!);

		var latestAliasResponse = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_alias/{LatestAlias}");
		latestAliasResponse.ApiCallDetails.HttpStatusCode.Should().Be(200,
			"latest alias should be created after applying aliases");

		var searchViaReadAlias = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{ReadAlias}/_search");
		searchViaReadAlias.ApiCallDetails.HttpStatusCode.Should().Be(200,
			"documents should be searchable via the read alias");
		searchViaReadAlias.Body.Should().Contain("\"CAT-001\"");
	}
}
