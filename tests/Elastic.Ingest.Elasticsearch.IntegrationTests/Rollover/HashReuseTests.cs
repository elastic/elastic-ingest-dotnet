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

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Rollover;

/*
 * Tests: Hash-based template reuse across multiple IngestChannel instances
 *
 * Document: ProductCatalog (Elastic.Mapping)
 *   Entity: Index  Name="idx-products"  [ContentHash] on content_hash field
 *
 *   ┌──────────────────────────────────────────────────────────────────┐
 *   │  Channel 1: bootstrap + write "HR-001" → hash₁                  │
 *   │  Channel 2: bootstrap (same context)   → hash₂ == hash₁         │
 *   │             write "HR-002" to SAME idx-products index            │
 *   │  _search idx-products → contains both HR-001 and HR-002          │
 *   └──────────────────────────────────────────────────────────────────┘
 *
 * Provisioning: HashBasedReuseProvisioning
 *   Identical mappings + settings → same ChannelHash → same template reused
 */
[NotInParallel("idx-products")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class HashReuseTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string Prefix = "idx-products";
	private const string IndexName = "idx-products";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync(Prefix);

	[After(Test)]
	public async Task Teardown() => await CleanupPrefixAsync(Prefix);

	[Test]
	public async Task SameMappingHashReusesTemplate()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;

		var slim1 = new CountdownEvent(1);
		var options1 = new IngestChannelOptions<ProductCatalog>(Client.Transport, ctx)
		{
			BufferOptions = new BufferOptions { WaitHandle = slim1, OutboundBufferMaxSize = 1 }
		};
		var channel1 = new IngestChannel<ProductCatalog>(options1);

		(await channel1.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();
		var hash1 = channel1.ChannelHash;

		channel1.TryWrite(new ProductCatalog
		{
			Sku = "HR-001", Name = "Hash Reuse Widget", Description = "First write",
			Category = "widgets", Price = 10.0, Tags = ["test"]
		});
		if (!slim1.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("First write timed out");

		var slim2 = new CountdownEvent(1);
		var options2 = new IngestChannelOptions<ProductCatalog>(Client.Transport, ctx)
		{
			BufferOptions = new BufferOptions { WaitHandle = slim2, OutboundBufferMaxSize = 1 }
		};
		var channel2 = new IngestChannel<ProductCatalog>(options2);
		(await channel2.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();
		var hash2 = channel2.ChannelHash;

		hash2.Should().Be(hash1,
			"same mapping hash should produce the same channel hash");

		channel2.TryWrite(new ProductCatalog
		{
			Sku = "HR-002", Name = "Hash Reuse Widget 2", Description = "Second write",
			Category = "widgets", Price = 20.0, Tags = ["test"]
		});
		if (!slim2.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("Second write timed out");

		await Client.Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{IndexName}/_refresh");

		var search = await Client.Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{IndexName}/_search");
		search.ApiCallDetails.HttpStatusCode.Should().Be(200);
		search.Body.Should().Contain("\"HR-001\"");
		search.Body.Should().Contain("\"HR-002\"");
	}
}
