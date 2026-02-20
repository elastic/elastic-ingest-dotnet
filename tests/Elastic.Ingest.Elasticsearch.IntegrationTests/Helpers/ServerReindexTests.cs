// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.Helpers;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Helpers;

/*
 * Tests: ServerReindex — async server-side _reindex with task monitoring
 *
 * Source: src/Elastic.Ingest.Elasticsearch/Helpers/ServerReindex.cs
 *
 * Exercises the full _reindex lifecycle:
 *   POST /_reindex?wait_for_completion=false → task ID
 *   GET  /_tasks/{taskId}                    → poll until completed
 *
 *   ┌──────────────────────────────────────────────────────────────┐
 *   │  1. Seed 10 docs into "reindex-src" via IndexChannel         │
 *   │  2. ServerReindex("reindex-src" → "reindex-dst").RunAsync()  │
 *   │     ├── POST /_reindex?wait_for_completion=false             │
 *   │     └── Poll /_tasks/{id} until completed                    │
 *   │  3. Refresh reindex-dst                                      │
 *   │  4. _search reindex-dst → 10 docs                            │
 *   └──────────────────────────────────────────────────────────────┘
 *
 *   5. ServerReindex with query filter (price >= 25)
 *   │  → only matching docs reindexed into "reindex-filtered"
 */
[NotInParallel("helpers")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class ServerReindexTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	[Before(Test)]
	public async Task Setup()
	{
		await CleanupPrefixAsync("reindex-src");
		await CleanupPrefixAsync("reindex-dst");
		await CleanupPrefixAsync("reindex-filtered");
	}

	[Test]
	public async Task ReindexCopiesAllDocuments()
	{
		await SeedDocuments("reindex-src", 10);

		var reindex = new ServerReindex(Transport, new ServerReindexOptions
		{
			Source = "reindex-src",
			Destination = "reindex-dst",
			PollInterval = TimeSpan.FromMilliseconds(500)
		});

		var result = await reindex.RunAsync();
		result.IsCompleted.Should().BeTrue();
		result.Error.Should().BeNull();
		(result.Created + result.Updated).Should().Be(10, "all 10 docs should be transferred (created or updated)");

		await RefreshAndAssertCount("reindex-dst", 10);
	}

	[Test]
	public async Task ReindexWithQueryFiltersCopiedDocuments()
	{
		await SeedDocuments("reindex-src", 10);

		var reindex = new ServerReindex(Transport, new ServerReindexOptions
		{
			Source = "reindex-src",
			Destination = "reindex-filtered",
			Query = """{ "range": { "price": { "gte": 25 } } }""",
			PollInterval = TimeSpan.FromMilliseconds(500)
		});

		var result = await reindex.RunAsync();
		result.IsCompleted.Should().BeTrue();
		result.Error.Should().BeNull();
		var transferred = result.Created + result.Updated;
		transferred.Should().Be(6, "prices 25, 30, 35, 40, 45, 50 match gte:25");

		await RefreshAndAssertCount("reindex-filtered", 6);
	}

	private async Task SeedDocuments(string index, int count)
	{
		var slim = new CountdownEvent(1);
		var options = new IndexChannelOptions<ProductCatalog>(Transport)
		{
			IndexFormat = index,
			BulkOperationIdLookup = p => p.Sku,
			BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = count }
		};
		var channel = new IndexChannel<ProductCatalog>(options);
		await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

		for (var i = 0; i < count; i++)
		{
			channel.TryWrite(new ProductCatalog
			{
				Sku = $"REIDX-{i:D3}",
				Name = $"Reindex Widget {i}",
				Description = "Test document for reindex.",
				Category = "reindex-test",
				Price = (i + 1) * 5.0, // start at 5 — Price=0.0 is omitted as default(double)
				Tags = ["reindex"]
			});
		}

		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("Seed documents were not persisted within 10 seconds");

		var refresh = await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{index}/_refresh");
		refresh.ApiCallDetails.HttpStatusCode.Should().Be(200);
	}

	private async Task RefreshAndAssertCount(string index, long expected)
	{
		await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{index}/_refresh");

		var search = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{index}/_count");
		search.ApiCallDetails.HttpStatusCode.Should().Be(200);
		search.Body.Should().Contain($"\"count\":{expected}");
	}
}
