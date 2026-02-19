// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Ingest.Elasticsearch.Catalog;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Strategies;
using FluentAssertions;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

/*
 * Tests: Built-in content-hash scripted upserts via CatalogIndexChannel<HashableArticle>
 *
 * Document type: HashableArticle (Elastic.Mapping)
 *   [Id][Keyword]       id              — bulk operation routing
 *   [Text]              title           — analyzer: "html_content" (standard + html_strip + lowercase + asciifolding)
 *   [ContentHash]       hash            — drives NOOP detection in scripted upserts
 *   [Date]              index_batch_date, last_updated
 *
 * Mappings & analysis flow:
 *   TestMappingContext.HashableArticle.Context
 *     ├── GetMappingsJson()      → field types + analyzer refs
 *     └── ConfigureAnalysis      → html_content analyzer, html_stripper char filter
 *   IngestStrategies.Index<>()
 *     └── GetMappingSettings     → merged entity settings + analysis JSON
 *
 * Bootstrap (CatalogIndexChannel):
 *   BootstrapElasticsearchAsync(Failure)
 *   ├── ComponentTemplate  update-data-template  (settings + analysis)
 *   ├── ComponentTemplate  update-data-template  (mappings)
 *   └── IndexTemplate      update-data-template  (pattern: update-data-*)
 *
 * Index naming:
 *   Format:  update-data-{yyyy.MM.dd.HH-mm-ss-fffffff}
 *   Alias:   update-data-search  →  latest concrete index
 *            update-data-latest  →  latest concrete index
 *
 * Scripted upsert sequence:
 *   ┌──────────────────────────────────────────────────────────────┐
 *   │ 1. Write 100 docs            → all created    (version = 1) │
 *   │ 2. Write 100 identical docs  → hash matches   (version = 1) │
 *   │ 3. Modify 50 docs' titles    → hash differs   (version = 2) │
 *   │ 4. New channel, same prefix  → reuses index via hash match  │
 *   │ 5. Modify same 50 again      → hash differs   (version = 3) │
 *   │ 6. Channel w/o scripted hash → forces new index (version=1) │
 *   └──────────────────────────────────────────────────────────────┘
 */
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class ScriptedHashUpdateIngestionTests(IngestionCluster cluster)
	: IntegrationTestBase(cluster)
{
	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync("update-data");

	[After(Test)]
	public async Task Teardown() => await CleanupPrefixAsync("update-data");

	[Test]
	public async Task EnsureDocumentsEndUpInIndex()
	{
		var indexPrefix = "update-data-";
		var slim = new CountdownEvent(1);
		var channel = CreateChannel(indexPrefix, slim);
		var bootstrapped = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		bootstrapped.Should().BeTrue("Expected to be able to bootstrap index channel");

		var indexName = channel.IndexName;
		var searchIndex = channel.Options.ActiveSearchAlias;

		var index = await Client.Indices.GetAsync(new GetIndexRequest(indexName));
		index.Indices.Should().BeNullOrEmpty();

		await WriteInitialDocuments(channel, slim, searchIndex, indexName, expectedVersion: 1);

		index = await Client.Indices.GetAsync(new GetIndexRequest(indexName));
		index.Indices.Should().NotBeNullOrEmpty();
		var lifecycle = index.Indices[indexName].Settings?.Index?.Lifecycle;
		if (lifecycle?.Name is not null)
			lifecycle.Name.Should().Be("7-days-default");

		await WriteInitialDocuments(channel, slim, searchIndex, indexName, expectedVersion: 1);

		var refreshResult = await Client.Indices.RefreshAsync(indexName);
		refreshResult.IsValidResponse.Should().BeTrue("{0}", refreshResult.DebugInformation);

		await UpdateHalfOfDocuments(channel, slim, searchIndex, indexName, expectedVersion: 2);

		channel = CreateChannel(indexPrefix, slim);
		await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		channel.IndexName.Should().Be(indexName);
		await UpdateHalfOfDocuments(channel, slim, searchIndex, indexName, expectedVersion: 3);

		channel = CreateChannel(indexPrefix, slim, useScriptedHashBulkUpsert: false);
		await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		channel.IndexName.Should().NotBe(indexName);
		indexName = channel.IndexName;
		await UpdateHalfOfDocuments(channel, slim, searchIndex, indexName, expectedVersion: 1);

	}

	private async Task WriteInitialDocuments(CatalogIndexChannel<HashableArticle> channel, CountdownEvent slim, string searchIndex, string indexName, int expectedVersion)
	{
		for (var i = 0; i < 100; i++)
			channel.TryWrite(new HashableArticle { Title = "Hello World!", Id = $"hello-world-{i}" });
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"ecs document was not persisted within 10 seconds: {channel}");
		slim.Reset();
		await channel.RefreshAsync();
		await channel.ApplyAliasesAsync();

		var searchResult = await Client.SearchAsync<HashableArticle>(s => s
			.Indices(searchIndex)
			.Aggregations(a => a.Add("max", a1 => a1.Max(m => m.Field("_version"))))
		);
		searchResult.Total.Should().Be(100);
		var maxVersion = searchResult.Aggregations?.GetMax("max")?.Value;
		maxVersion.Should().Be(expectedVersion);

		var storedDocument = searchResult.Documents.First();
		storedDocument.Id.Should().StartWith("hello-world");
		storedDocument.Title.Should().Be("Hello World!");

		var hit = searchResult.Hits.First();
		hit.Index.Should().Be(indexName);

	}

	private async Task UpdateHalfOfDocuments(CatalogIndexChannel<HashableArticle> channel, CountdownEvent slim, string searchIndex, string indexName, int expectedVersion)
	{
		for (var i = 0; i < 100; i++)
		{
			var title = "Hello World!";
			if (i % 2 == 0)
				title += $"{i:N0}-{expectedVersion:N0}";
			channel.TryWrite(new HashableArticle { Title = title , Id = $"hello-world-{i}" });
		}
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"Updates did not go through within 10s: {channel}");

		slim.Reset();
		await channel.RefreshAsync();
		await channel.ApplyAliasesAsync();
		var searchResult = await Client.SearchAsync<HashableArticle>(s => s
			.Indices(searchIndex)
			.Aggregations(a => a
				.Add("max", a1 => a1.Max(m => m.Field("_version")))
				.Add("terms", a1 => a1.Terms(m => m.Field("_version")))
			)
		);
		searchResult.Total.Should().Be(100);
		var maxVersion = searchResult.Aggregations?.GetMax("max")?.Value;
		maxVersion.Should().Be(expectedVersion);

		var hit = searchResult.Hits.First();
		hit.Index.Should().Be(indexName);

		var terms = searchResult.Aggregations?.GetLongTerms("terms")?.Buckets;
		if (expectedVersion > 1)
		{
			terms.Should().NotBeNullOrEmpty().And.HaveCount(2);
			terms.Should().Contain(t => t.Key == 1).Subject.DocCount.Should().Be(50);
			terms.Should().Contain(t => t.Key == expectedVersion).Subject.DocCount.Should().Be(50);
		}
		else
		{
			terms.Should().NotBeNullOrEmpty().And.HaveCount(1);
			terms.Should().Contain(t => t.Key == 1).Subject.DocCount.Should().Be(100);
		}
	}

	private CatalogIndexChannel<HashableArticle> CreateChannel(string indexPrefix, CountdownEvent slim, bool useScriptedHashBulkUpsert = true)
	{
		var ctx = TestMappingContext.HashableArticle.Context;
		var strategy = IngestStrategies.Index<HashableArticle>(ctx);

		var options = new CatalogIndexChannelOptions<HashableArticle>(Client.Transport)
		{
			IndexFormat = indexPrefix + "{0:yyyy.MM.dd.HH-mm-ss-fffffff}",
			ActiveSearchAlias = indexPrefix + "search",
			BulkOperationIdLookup = c => c.Id,
			ScriptedHashBulkUpsertLookup = !useScriptedHashBulkUpsert ? null : (c, channelHash) =>
			{
				var hash = HashedBulkUpdate.CreateHash(channelHash, c.Id, c.Title ?? string.Empty);
				c.Hash = hash;
				return new HashedBulkUpdate("hash", hash);
			},
			BufferOptions = new BufferOptions
			{
				WaitHandle = slim, OutboundBufferMaxSize = 100,
			},
			GetMapping = ctx.GetMappingsJson,
			GetMappingSettings = strategy.GetMappingSettings
		};
		return new CatalogIndexChannel<HashableArticle>(options);
	}
}
