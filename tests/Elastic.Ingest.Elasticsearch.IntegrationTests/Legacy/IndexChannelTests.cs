// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Legacy;

/*
 * Tests: Legacy IndexChannel<T> API (pre-IngestStrategy)
 *
 * Unlike IngestChannel<T> which delegates to IIngestStrategy, the older
 * IndexChannel<T> owns its own bootstrap and bulk routing directly.
 *
 * Document: ProductCatalog
 *   IndexFormat: "idx-legacy" (fixed name, no date pattern)
 *
 * Bootstrap (IndexChannel):
 *   BootstrapElasticsearchAsync(Failure)
 *   ├── PUT /_component_template/idx-legacy-settings           (default settings)
 *   ├── PUT /_component_template/idx-legacy-mappings           (default mappings, no custom analysis)
 *   └── PUT /_index_template/idx-legacy                        (pattern: idx-legacy-*)
 *
 *   ┌─────────────────────────────────────────────────────────┐
 *   │  IndexChannel<ProductCatalog>                           │
 *   │  ├── Bootstrap templates (idx-legacy)                   │
 *   │  ├── TryWrite(product) ─→ _bulk to idx-legacy           │
 *   │  └── Verify via _search on idx-legacy                   │
 *   └─────────────────────────────────────────────────────────┘
 *
 * No custom analysis:  The old channel API does not inject Elastic.Mapping analysis;
 *                      it uses default component settings only.
 * No aliases:          IndexChannel does not support alias management.
 */
[NotInParallel("legacy-channels")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class IndexChannelTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string Prefix = "idx-legacy";
	private const string IndexName = "idx-legacy";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync(Prefix);

	[Test]
	public async Task BootstrapAndIngestViaLegacyIndexChannel()
	{
		var slim = new CountdownEvent(1);
		var options = new IndexChannelOptions<ProductCatalog>(Transport)
		{
			IndexFormat = IndexName,
			BulkOperationIdLookup = p => p.Sku,
			BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 1 }
		};
		var channel = new IndexChannel<ProductCatalog>(options);

		var bootstrapped = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		bootstrapped.Should().BeTrue("IndexChannel bootstrap should succeed");

		channel.TryWrite(new ProductCatalog
		{
			Sku = "LEG-001",
			Name = "Legacy Channel Widget",
			Description = "Written via the old IndexChannel API.",
			Category = "widgets",
			Price = 5.99,
			Tags = ["legacy", "test"]
		});

		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"Document was not persisted within 10 seconds: {channel}");

		var refresh = await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{IndexName}/_refresh");
		refresh.ApiCallDetails.HttpStatusCode.Should().Be(200);

		var search = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{IndexName}/_search");
		search.ApiCallDetails.HttpStatusCode.Should().Be(200);
		search.Body.Should().Contain("\"LEG-001\"");
		search.Body.Should().Contain("Legacy Channel Widget");
	}

	[Test]
	public async Task ChannelHashIsStableAcrossIdenticalChannels()
	{
		var options = new IndexChannelOptions<ProductCatalog>(Transport)
		{
			IndexFormat = IndexName,
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};

		var ch1 = new IndexChannel<ProductCatalog>(options);
		(await ch1.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();
		var hash1 = ch1.ChannelHash;

		var ch2 = new IndexChannel<ProductCatalog>(options);
		(await ch2.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();
		var hash2 = ch2.ChannelHash;

		hash1.Should().Be(hash2, "identical IndexChannel options should produce the same hash");
	}
}
