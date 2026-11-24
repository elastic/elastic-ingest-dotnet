// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.Strategies;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.SingleIndex;

/*
 * Use case: Single index — additive mapping patch of an existing, fixed-name index
 * Tests:    Bootstrapping V2 of a type adds the fields V1's index is missing (is_featured, discount_eligible),
 *           and only those; opting out leaves the index alone.
 *
 * Templates only apply to newly created indices, so without IndexMappingPatchStep the live index
 * never learns about fields added to the type (see MappingEvolutionTests).
 */
[NotInParallel("single-index")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class MappingPatchTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string Prefix = "idx-products";
	private const string IndexName = "idx-products";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync(Prefix);

	[After(Test)]
	public async Task Teardown() => await CleanupPrefixAsync(Prefix);

	private async Task CreateV1IndexAsync()
	{
		var slim = new CountdownEvent(1);
		var channelV1 = new IngestChannel<ProductCatalog>(new IngestChannelOptions<ProductCatalog>(Transport, TestMappingContext.ProductCatalog.Context)
		{
			BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 1 }
		});
		(await channelV1.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();
		channelV1.TryWrite(new ProductCatalog
		{
			Sku = "PATCH-001", Name = "Patch Widget V1", Description = "Initial version",
			Category = "Widgets", Price = 15.0, Tags = ["v1"]
		});
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("V1 write timed out");
		await Transport.RequestAsync<StringResponse>(HttpMethod.POST, $"/{IndexName}/_refresh");

		var mapping = await Transport.RequestAsync<StringResponse>(HttpMethod.GET, $"/{IndexName}/_mapping");
		mapping.Body.Should().NotContain("is_featured", "V1 has no such field");
	}

	[Test]
	public async Task BootstrapAddsMissingFieldsToTheExistingIndex()
	{
		await CreateV1IndexAsync();

		var channelV2 = new IngestChannel<ProductCatalogV2>(new IngestChannelOptions<ProductCatalogV2>(
			Transport, TestMappingContext.ProductCatalogV2.Context));
		(await channelV2.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();

		var mapping = await Transport.RequestAsync<StringResponse>(HttpMethod.GET, $"/{IndexName}/_mapping");
		mapping.Body.Should().Contain("\"is_featured\":{\"type\":\"boolean\"}", "the new field was patched in");
		mapping.Body.Should().Contain("\"discount_eligible\"", "the new runtime field was patched in");
		mapping.Body.Should().Contain("\"price_tier\"", "fields the type no longer maps are left alone");
		mapping.Body.Should().Contain("\"sku\"");

		// the original document survives and the index accepts documents using the new field
		var slim = new CountdownEvent(1);
		var writer = new IngestChannel<ProductCatalogV2>(new IngestChannelOptions<ProductCatalogV2>(
			Transport, TestMappingContext.ProductCatalogV2.Context)
		{
			BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 1 }
		});
		writer.TryWrite(new ProductCatalogV2
		{
			Sku = "PATCH-002", Name = "Patch Widget V2", Description = "Updated version",
			Category = "Widgets", Price = 8.0, Tags = ["v2"], IsFeatured = true
		});
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("V2 write timed out");
		await Transport.RequestAsync<StringResponse>(HttpMethod.POST, $"/{IndexName}/_refresh");

		var featured = await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{IndexName}/_search", PostData.String("""{"query":{"term":{"is_featured":true}}}"""));
		featured.Body.Should().Contain("\"PATCH-002\"").And.NotContain("\"PATCH-001\"");
		var all = await Transport.RequestAsync<StringResponse>(HttpMethod.GET, $"/{IndexName}/_search");
		all.Body.Should().Contain("\"PATCH-001\"");
	}

	[Test]
	public async Task SecondBootstrapFindsNothingLeftToPatch()
	{
		await CreateV1IndexAsync();

		var options = new IngestChannelOptions<ProductCatalogV2>(Transport, TestMappingContext.ProductCatalogV2.Context);
		(await new IngestChannel<ProductCatalogV2>(options).BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();

		var before = await Transport.RequestAsync<StringResponse>(HttpMethod.GET, $"/{IndexName}/_mapping");
		(await new IngestChannel<ProductCatalogV2>(options).BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();
		var after = await Transport.RequestAsync<StringResponse>(HttpMethod.GET, $"/{IndexName}/_mapping");

		after.Body.Should().Be(before.Body);
	}

	[Test]
	public async Task OptingOutLeavesTheExistingIndexUntouched()
	{
		await CreateV1IndexAsync();

		var strategy = IngestStrategies.Index<ProductCatalogV2>(
			TestMappingContext.ProductCatalogV2.Context, BootstrapStrategies.Index(patchExistingMappings: false));
		var channel = new IngestChannel<ProductCatalogV2>(
			new IngestChannelOptions<ProductCatalogV2>(Transport, strategy));
		(await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();

		var mapping = await Transport.RequestAsync<StringResponse>(HttpMethod.GET, $"/{IndexName}/_mapping");
		mapping.Body.Should().NotContain("is_featured");
	}
}
