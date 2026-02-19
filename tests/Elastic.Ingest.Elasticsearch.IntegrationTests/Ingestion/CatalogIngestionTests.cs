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

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Ingestion;

[NotInParallel("cat-products")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class CatalogIngestionTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
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
		var options = new IngestChannelOptions<ProductCatalog>(Client.Transport, ctx)
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
		var resolveResponse = await Client.Transport.RequestAsync<StringResponse>(
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
		var refresh = await Client.Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{concreteIndex}/_refresh");
		refresh.ApiCallDetails.HasSuccessfulStatusCode.Should().BeTrue();

		// Search to verify the document landed
		var searchViaPattern = await Client.Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{concreteIndex}/_search");
		searchViaPattern.ApiCallDetails.HttpStatusCode.Should().Be(200);
		searchViaPattern.Body.Should().Contain("\"CAT-001\"");

		// Apply aliases via the channel using the concrete index name
		await channel.ApplyAliasesAsync(concreteIndex!);

		var latestAliasResponse = await Client.Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_alias/{LatestAlias}");
		latestAliasResponse.ApiCallDetails.HttpStatusCode.Should().Be(200,
			"latest alias should be created after applying aliases");

		var searchViaReadAlias = await Client.Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{ReadAlias}/_search");
		searchViaReadAlias.ApiCallDetails.HttpStatusCode.Should().Be(200,
			"documents should be searchable via the read alias");
		searchViaReadAlias.Body.Should().Contain("\"CAT-001\"");
	}
}
