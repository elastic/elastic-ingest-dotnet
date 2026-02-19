// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Ingestion;

[NotInParallel("idx-products")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class IndexIngestionTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string Prefix = "idx-products";
	private const string IndexName = "idx-products";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync(Prefix);

	[After(Test)]
	public async Task Teardown() => await CleanupPrefixAsync(Prefix);

	[Test]
	public async Task EnsureDocumentsEndUpInIndex()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
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
			Sku = "WDG-001",
			Name = "Premium Carbon Widget",
			Description = "A high-quality premium widget made from carbon fiber.",
			Category = "widgets",
			Price = 49.99,
			Tags = ["premium", "carbon", "widget"]
		});

		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"Document was not persisted within 10 seconds: {channel}");

		var refresh = await Client.Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{IndexName}/_refresh");
		refresh.ApiCallDetails.HttpStatusCode.Should().Be(200);

		var search = await Client.Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{IndexName}/_search");
		search.ApiCallDetails.HttpStatusCode.Should().Be(200);
		search.Body.Should().Contain("\"WDG-001\"");
		search.Body.Should().Contain("Premium Carbon Widget");
	}
}
