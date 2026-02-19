// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.Strategies;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Bootstrap;

[NotInParallel("idx-products")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class IndexBootstrapTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string Prefix = "idx-products";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync(Prefix);

	[After(Test)]
	public async Task Teardown() => await CleanupPrefixAsync(Prefix);

	[Test]
	public async Task BootstrapCreatesComponentAndIndexTemplates()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		var options = new IngestChannelOptions<ProductCatalog>(Client.Transport, ctx)
		{
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<ProductCatalog>(options);

		var result = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		result.Should().BeTrue();

		var mappingsTemplate = await Client.Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_component_template/{Prefix}-template-mappings");
		mappingsTemplate.ApiCallDetails.HttpStatusCode.Should().Be(200);
		mappingsTemplate.Body.Should().Contain("\"sku\"");
		mappingsTemplate.Body.Should().Contain("\"keyword\"");
		mappingsTemplate.Body.Should().Contain("\"product_autocomplete\"");

		var indexTemplate = await Client.Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_index_template/{Prefix}-template");
		indexTemplate.ApiCallDetails.HttpStatusCode.Should().Be(200);
	}

	[Test]
	public async Task WithIlmBootstrapCreatesIlmPolicy()
	{
		var ilmCheck = await Client.Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, "/_ilm/status");
		if (ilmCheck.ApiCallDetails.HttpStatusCode != 200)
			return; // ILM not available on this cluster (e.g. serverless)

		var ctx = TestMappingContext.ProductCatalog.Context;
		var strategy = IngestStrategies.Index<ProductCatalog>(ctx,
			BootstrapStrategies.IndexWithIlm("idx-products-policy"));
		var options = new IngestChannelOptions<ProductCatalog>(Client.Transport, strategy, ctx)
		{
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<ProductCatalog>(options);

		var result = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		result.Should().BeTrue();

		var ilmResponse = await Client.Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, "/_ilm/policy/idx-products-policy");
		ilmResponse.ApiCallDetails.HttpStatusCode.Should().Be(200);
		ilmResponse.Body.Should().Contain("idx-products-policy");
	}
}
