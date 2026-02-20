// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
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
 * Tests: PointInTimeSearch<T> — PIT-based search_after pagination
 *
 * Source: src/Elastic.Ingest.Elasticsearch/Helpers/PointInTimeSearch.cs
 *
 * Exercises the complete PIT lifecycle:
 *   POST /{index}/_pit?keep_alive=5m           → open PIT
 *   POST /_search (pit + search_after + sort)   → paginate
 *   DELETE /_pit                                 → close PIT
 *
 *   ┌──────────────────────────────────────────────────────────────┐
 *   │  1. Seed 25 docs into "pit-test" via IndexChannel            │
 *   │  2. PointInTimeSearch<ProductCatalog>(Size = 10)             │
 *   │     ├── Page 1: 10 docs                                      │
 *   │     ├── Page 2: 10 docs                                      │
 *   │     └── Page 3:  5 docs  (HasMore = false)                   │
 *   │  3. Verify total = 25 unique SKUs collected                   │
 *   └──────────────────────────────────────────────────────────────┘
 *
 *   4. SearchDocumentsAsync flattens pages into individual docs
 *      → verify 25 documents yielded
 */
[NotInParallel("helpers")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class PointInTimeSearchTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string IndexName = "pit-test";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync("pit-test");

	[Test]
	public async Task PaginatesThroughAllDocuments()
	{
		await SeedDocuments(25);

		await using var pit = new PointInTimeSearch<ProductCatalog>(
			Transport,
			new PointInTimeSearchOptions
			{
				Index = IndexName,
				Size = 10,
				KeepAlive = "1m"
			});

		var pages = new List<SearchPage<ProductCatalog>>();
		await foreach (var page in pit.SearchPagesAsync())
			pages.Add(page);

		pages.Should().HaveCount(3, "25 docs / 10 per page = 3 pages");
		pages[0].Documents.Should().HaveCount(10);
		pages[1].Documents.Should().HaveCount(10);
		pages[2].Documents.Should().HaveCount(5);
		pages[2].HasMore.Should().BeFalse();
		pages[0].TotalDocuments.Should().Be(25);
	}

	[Test]
	public async Task FlattenedSearchReturnsAllDocuments()
	{
		await SeedDocuments(25);

		await using var pit = new PointInTimeSearch<ProductCatalog>(
			Transport,
			new PointInTimeSearchOptions
			{
				Index = IndexName,
				Size = 10,
				KeepAlive = "1m"
			});

		var allDocs = new List<ProductCatalog>();
		await foreach (var doc in pit.SearchDocumentsAsync())
			allDocs.Add(doc);

		allDocs.Should().HaveCount(25);
	}

	[Test]
	public async Task QueryFilterLimitsResults()
	{
		await SeedDocuments(25);

		await using var pit = new PointInTimeSearch<ProductCatalog>(
			Transport,
			new PointInTimeSearchOptions
			{
				Index = IndexName,
				Size = 100,
				KeepAlive = "1m",
				QueryBody = """{ "range": { "price": { "gte": 100 } } }"""
			});

		var allDocs = new List<ProductCatalog>();
		await foreach (var doc in pit.SearchDocumentsAsync())
			allDocs.Add(doc);

		allDocs.Should().HaveCount(6, "prices 100, 105, 110, 115, 120, 125 match gte:100");
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
				Sku = $"PIT-{i:D3}",
				Name = $"PIT Widget {i}",
				Description = "Test document for PIT search.",
				Category = "pit-test",
				Price = (i + 1) * 5.0, // start at 5 — Price=0.0 is omitted as default(double)
				Tags = ["pit"]
			});
		}

		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("Seed documents were not persisted within 10 seconds");

		var refresh = await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{IndexName}/_refresh");
		refresh.ApiCallDetails.HttpStatusCode.Should().Be(200);
	}
}
