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
 * Tests: DeleteByQuery — async _delete_by_query with task monitoring
 *
 * Source: src/Elastic.Ingest.Elasticsearch/Helpers/DeleteByQuery.cs
 *
 * Exercises the full _delete_by_query lifecycle:
 *   POST /{index}/_delete_by_query?wait_for_completion=false → task ID
 *   GET  /_tasks/{taskId}                                    → poll until completed
 *
 *   ┌──────────────────────────────────────────────────────────────┐
 *   │  1. Seed 10 docs into "dbq-test" via IndexChannel            │
 *   │     Prices: 5, 10, 15, 20, 25, 30, 35, 40, 45, 50           │
 *   │     (starts at 5 — Price=0.0 is omitted as default(double)) │
 *   │  2. DeleteByQuery (price < 25) on "dbq-test"                 │
 *   │     ├── POST /dbq-test/_delete_by_query?wait_for_completion  │
 *   │     └── Poll /_tasks/{id} until completed                    │
 *   │  3. Refresh → _count → 6 remaining                           │
 *   └──────────────────────────────────────────────────────────────┘
 */
[NotInParallel("helpers")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class DeleteByQueryTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string IndexName = "dbq-test";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync("dbq-test");

	[Test]
	public async Task DeleteByQueryRemovesMatchingDocuments()
	{
		await SeedDocuments(10);

		var dbq = new DeleteByQuery(Transport, new DeleteByQueryOptions
		{
			Index = IndexName,
			QueryBody = """{ "range": { "price": { "lt": 25 } } }""",
			PollInterval = TimeSpan.FromMilliseconds(500)
		});

		var result = await dbq.RunAsync();
		result.IsCompleted.Should().BeTrue();
		result.Error.Should().BeNull();
		result.Deleted.Should().Be(4, "prices 5, 10, 15, 20 are < 25");

		await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{IndexName}/_refresh");

		var count = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{IndexName}/_count");
		count.ApiCallDetails.HttpStatusCode.Should().Be(200);
		count.Body.Should().Contain("\"count\":6");
	}

	private async Task SeedDocuments(int count)
	{
		var slim = new CountdownEvent(1);
		var options = new IndexChannelOptions<ProductCatalog>(Transport)
		{
			IndexFormat = IndexName,
			BulkOperationIdLookup = p => p.Sku,
			BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = count }
		};
		var channel = new IndexChannel<ProductCatalog>(options);
		await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

		for (var i = 0; i < count; i++)
		{
			channel.TryWrite(new ProductCatalog
			{
				Sku = $"DBQ-{i:D3}",
				Name = $"Delete Widget {i}",
				Description = "Test document for delete by query.",
				Category = "dbq-test",
				Price = (i + 1) * 5.0, // start at 5 — Price=0.0 is omitted as default(double)
				Tags = ["dbq"]
			});
		}

		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("Seed documents were not persisted within 10 seconds");

		var refresh = await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{IndexName}/_refresh");
		refresh.ApiCallDetails.HttpStatusCode.Should().Be(200);
	}
}
