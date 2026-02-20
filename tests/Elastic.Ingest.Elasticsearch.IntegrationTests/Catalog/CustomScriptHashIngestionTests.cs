// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Ingest.Elasticsearch.Catalog;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Strategies;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Catalog;

/*
 * Tests: Custom Painless script in scripted hash upserts via CatalogIndexChannel<HashableArticle>
 *
 * Document type: HashableArticle (Elastic.Mapping)
 *   Same as ScriptedHashUpdateIngestionTests — shared type,
 *   but this test supplies a CUSTOM Painless script that always
 *   updates (no NOOP), plus tracks index_batch_date per-document.
 *
 * Mappings & analysis:
 *   Identical to ScriptedHashUpdateIngestionTests — sourced from
 *   TestMappingContext.HashableArticle.Context via Elastic.Mapping.
 *
 * Bootstrap (CatalogIndexChannel):
 *   BootstrapElasticsearchAsync(Failure)
 *   ├── ComponentTemplate  scripted-data-template  (settings + analysis)
 *   ├── ComponentTemplate  scripted-data-template  (mappings)
 *   └── IndexTemplate      scripted-data-template  (pattern: scripted-data-*)
 *
 * Index naming:
 *   Format:  scripted-data-{yyyy.MM.dd.HH-mm-ss-fffffff}
 *   Alias:   scripted-data-search  →  latest concrete index
 *            scripted-data-latest  →  latest concrete index
 *
 * Custom script upsert flow:
 *   ┌──────────────────────────────────────────────────────────────────┐
 *   │ HashedBulkUpdate includes extra Painless:                       │
 *   │   "ctx._source.index_batch_date = params.index_batch_date"      │
 *   │                                                                  │
 *   │ 1. Write 100 docs (batch T₀)   → all created     (version = 1) │
 *   │ 2. Write 100 same (batch T₀)   → script UPDATES  (version = 2) │
 *   │    Unlike built-in hash script, custom script never NOOPs.      │
 *   │ 3. Write 90 docs (i≥10, T₁)    → script UPDATES  (version = 3) │
 *   │    Half get new title → index_batch_date changes for 90 docs.   │
 *   │    10 docs untouched keep T₀; 45 updated keep T₁.              │
 *   └──────────────────────────────────────────────────────────────────┘
 */
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class CustomScriptHashIngestionTests : IntegrationTestBase
{
	public CustomScriptHashIngestionTests(IngestionCluster cluster) : base(cluster)
	{
		IndexBatchDate = DateTimeOffset.UtcNow;
		IndexBatchDateUpdate = IndexBatchDate.AddHours(1);
		IndexPrefix = "scripted-data-";
		Slim = new CountdownEvent(1);
	}

	public string IndexPrefix { get; }
	public string IndexName { get; set; } = null!;
	public CountdownEvent Slim { get; }
	public CatalogIndexChannel<HashableArticle> Channel { get; set; } = null!;
	public DateTimeOffset IndexBatchDate { get; }
	public DateTimeOffset IndexBatchDateUpdate { get; }

	[Before(Test)]
	public async Task Setup()
	{
		await CleanupPrefixAsync("scripted-data");
		Channel = CreateChannel(IndexPrefix, Slim);
		await Channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		IndexName = Channel.IndexName;
	}

	[After(Test)]
	public Task Cleanup()
	{
		Channel?.Dispose();
		Slim?.Dispose();
		return Task.CompletedTask;
	}

	[Test]
	public async Task EnsureDocumentsEndUpInIndex()
	{
		var bootstrapped = await Channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		bootstrapped.Should().BeTrue("Expected to be able to bootstrap index channel");

		var searchIndex = Channel.Options.ActiveSearchAlias;

		var index = await Client.Indices.GetAsync(new GetIndexRequest(IndexName));
		index.Indices.Should().BeNullOrEmpty();

		await WriteInitialDocuments(searchIndex, IndexName, expectedVersion: 1);

		index = await Client.Indices.GetAsync(new GetIndexRequest(IndexName));
		index.Indices.Should().NotBeNullOrEmpty();
		var lifecycle = index.Indices[IndexName].Settings?.Index?.Lifecycle;
		if (lifecycle?.Name is not null)
			lifecycle.Name.Should().Be("7-days-default");

		await WriteInitialDocuments(searchIndex, IndexName, expectedVersion: 2);

		var refreshResult = await Client.Indices.RefreshAsync(IndexName);
		refreshResult.IsValidResponse.Should().BeTrue("{0}", refreshResult.DebugInformation);

		await UpdateHalfOfDocumentsSkipFirst10(searchIndex, IndexName, expectedVersion: 3);
	}

	private async Task WriteInitialDocuments(string searchIndex, string indexName, int expectedVersion)
	{
		for (var i = 0; i < 100; i++)
			Channel.TryWrite(new HashableArticle { Title = "Hello World!", Id = $"hello-world-{i}", IndexBatchDate = IndexBatchDate, LastUpdated = IndexBatchDate });
		if (!Slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"ecs document was not persisted within 10 seconds: {Channel}");

		Slim.Reset();
		await Channel.RefreshAsync();
		await Channel.ApplyAliasesAsync();

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

	private async Task UpdateHalfOfDocumentsSkipFirst10(string searchIndex, string indexName, int expectedVersion)
	{
		for (var i = 10; i < 100; i++)
		{
			var title = "Hello World!";
			if (i % 2 == 0)
				title += $"{i:N0}-{expectedVersion:N0}";
			Channel.TryWrite(new HashableArticle
			{
				Title = title , Id = $"hello-world-{i}", IndexBatchDate = IndexBatchDateUpdate, LastUpdated = IndexBatchDateUpdate
			});
		}
		if (!Slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"Updates did not go through within 10s: {Channel}");

		Slim.Reset();
		await Channel.RefreshAsync();
		await Channel.ApplyAliasesAsync();
		var searchResult = await Client.SearchAsync<HashableArticle>(s => s
			.Indices(searchIndex)
			.Size(1)
			.Aggregations(a => a
				.Add("max", a1 => a1.Max(m => m.Field("_version")))
				.Add("terms-index", a1 => a1.Terms(m => m.Field("index_batch_date")))
				.Add("terms-updated", a1 => a1.Terms(m => m.Field("last_updated")))
			)
		);
		searchResult.Total.Should().Be(100);
		var maxVersion = searchResult.Aggregations?.GetMax("max")?.Value;
		maxVersion.Should().Be(expectedVersion);

		var ingestDates = searchResult.Aggregations?.GetLongTerms("terms-index")?.Buckets;
		ingestDates.Should().NotBeNullOrEmpty().And.HaveCount(2);
		ingestDates.First(d => d.Key == IndexBatchDate.ToUnixTimeMilliseconds()).DocCount.Should().Be(10);
		ingestDates.First(d => d.Key == IndexBatchDateUpdate.ToUnixTimeMilliseconds()).DocCount.Should().Be(90);

		var updateDates = searchResult.Aggregations?.GetLongTerms("terms-updated")?.Buckets;
		updateDates.Should().NotBeNullOrEmpty().And.HaveCount(2);
		updateDates.First(d => d.Key == IndexBatchDate.ToUnixTimeMilliseconds()).DocCount.Should().Be(55);
		updateDates.First(d => d.Key == IndexBatchDateUpdate.ToUnixTimeMilliseconds()).DocCount.Should().Be(45);

		var hit = searchResult.Hits.First();
		hit.Index.Should().Be(indexName);

	}

	private CatalogIndexChannel<HashableArticle> CreateChannel(string indexPrefix, CountdownEvent slim, bool useScriptedHashBulkUpsert = true)
	{
		var ctx = TestMappingContext.HashableArticle.Context;
		var strategy = IngestStrategies.Index<HashableArticle>(ctx);

		var options = new CatalogIndexChannelOptions<HashableArticle>(Transport)
		{
			IndexFormat = indexPrefix + "{0:yyyy.MM.dd.HH-mm-ss-fffffff}",
			ActiveSearchAlias = indexPrefix + "search",
			BulkOperationIdLookup = c => c.Id,
			ScriptedHashBulkUpsertLookup = !useScriptedHashBulkUpsert ? null : (c, channelHash) =>
			{
				var hash = HashedBulkUpdate.CreateHash(channelHash, c.Id, c.Title ?? string.Empty);
				c.Hash = hash;
				return new HashedBulkUpdate("hash", hash, "ctx._source.index_batch_date = params.index_batch_date",
					new Dictionary<string, string>
					{
						{ "index_batch_date", c.IndexBatchDate.ToString("o") }
					});
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
