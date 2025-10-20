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
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

public class CustomScriptHashIngestionTests
	: IntegrationTestBase, IAsyncLifetime
{
	public CustomScriptHashIngestionTests(IngestionCluster cluster, ITestOutputHelper output) : base(cluster, output)
	{
		IndexBatchDate = DateTimeOffset.UtcNow;
		IndexBatchDateUpdate = IndexBatchDate.AddHours(1);
		IndexPrefix = "scripted-data-";
		Slim = new CountdownEvent(1);
		Channel = CreateChannel(IndexPrefix, Slim);
		IndexName = Channel.IndexName;
	}

	public string IndexPrefix { get; }
	public string IndexName { get; set; }
	public CountdownEvent Slim { get; }
	public CatalogIndexChannel<HashDocument> Channel { get; set; }
	public DateTimeOffset IndexBatchDate { get; }
	public DateTimeOffset IndexBatchDateUpdate { get; }

	/// <inheritdoc />
	public async Task InitializeAsync()
	{
		await Channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure, "7-days-default");
		IndexName = Channel.IndexName;
	}

	/// <inheritdoc />
	public Task DisposeAsync()
	{
		Channel.Dispose();
		return Task.CompletedTask;
	}

	[Fact]
	public async Task EnsureDocumentsEndUpInIndex()
	{
		var bootstrapped = await Channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure, "7-days-default");
		bootstrapped.Should().BeTrue("Expected to be able to bootstrap index channel");

		var searchIndex = Channel.Options.ActiveSearchAlias;

		// Verify the index does not exist yet
		var index = await Client.Indices.GetAsync(new GetIndexRequest(IndexName));
		index.Indices.Should().BeNullOrEmpty();

		await WriteInitialDocuments(searchIndex, IndexName, expectedVersion: 1);

		// Verify the index exists and has the correct settings
		index = await Client.Indices.GetAsync(new GetIndexRequest(IndexName));
		index.Indices.Should().NotBeNullOrEmpty();
		index.Indices[IndexName].Settings?.Index?.Lifecycle?.Name?.Should().NotBeNull().And.Be("7-days-default");

		// write the same 100 documents again over the same channel;
		// since we are using a custom script that doesn't noop all documents should be updated
		await WriteInitialDocuments(searchIndex, IndexName, expectedVersion: 2);

		var refreshResult = await Client.Indices.RefreshAsync(IndexName);
		refreshResult.IsValidResponse.Should().BeTrue("{0}", refreshResult.DebugInformation);

		// Now only update half the documents, assert the version is 3 for all the documents
		await UpdateHalfOfDocumentsSkipFirst10(searchIndex, IndexName, expectedVersion: 3);


	}

	private async Task WriteInitialDocuments(string searchIndex, string indexName, int expectedVersion)
	{
		// Write 100 documents
		for (var i = 0; i < 100; i++)
			Channel.TryWrite(new HashDocument { Title = "Hello World!", Id = $"hello-world-{i}", IndexBatchDate = IndexBatchDate, LastUpdated = IndexBatchDate });
		if (!Slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"ecs document was not persisted within 10 seconds: {Channel}");

		Slim.Reset();
		await Channel.RefreshAsync();
		await Channel.ApplyAliasesAsync();

		var searchResult = await Client.SearchAsync<HashDocument>(s => s
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
			Channel.TryWrite(new HashDocument
			{
				Title = title , Id = $"hello-world-{i}", IndexBatchDate = IndexBatchDateUpdate, LastUpdated = IndexBatchDateUpdate
			});
		}
		if (!Slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"Updates did not go through within 10s: {Channel}");

		Slim.Reset();
		await Channel.RefreshAsync();
		await Channel.ApplyAliasesAsync();
		var searchResult = await Client.SearchAsync<HashDocument>(s => s
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

		// since we skip 10 we only expect 90 to be part of this update
		var ingestDates = searchResult.Aggregations?.GetLongTerms("terms-index")?.Buckets;
		ingestDates.Should().NotBeNullOrEmpty().And.HaveCount(2);
		ingestDates!.First(d => d.Key == IndexBatchDate.ToUnixTimeMilliseconds()).DocCount.Should().Be(10);
		ingestDates!.First(d => d.Key == IndexBatchDateUpdate.ToUnixTimeMilliseconds()).DocCount.Should().Be(90);

		// since we update half of 90 we expect 45 documents to actually have the updated timestamp
		var updateDates = searchResult.Aggregations?.GetLongTerms("terms-updated")?.Buckets;
		updateDates.Should().NotBeNullOrEmpty().And.HaveCount(2);
		updateDates!.First(d => d.Key == IndexBatchDate.ToUnixTimeMilliseconds()).DocCount.Should().Be(55);
		updateDates!.First(d => d.Key == IndexBatchDateUpdate.ToUnixTimeMilliseconds()).DocCount.Should().Be(45);

		var hit = searchResult.Hits.First();
		hit.Index.Should().Be(indexName);

	}

	private CatalogIndexChannel<HashDocument> CreateChannel(string indexPrefix, CountdownEvent slim, bool useScriptedHashBulkUpsert = true)
	{
		var options = new CatalogIndexChannelOptions<HashDocument>(Client.Transport)
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

			// language=json
			GetMappingSettings = () =>
				"""
				{
				  "analysis": {
				    "analyzer": {
				      "my_analyzer": {
				        "type": "custom",
				        "tokenizer": "standard",
				        "char_filter": [ "html_strip" ],
				        "filter": [ "lowercase", "asciifolding" ]
				      }
				    }
				  }
				}
				""",
			// language=json
			GetMapping = () =>
				"""
				{
				  "properties": {
					"hash": { "type": "keyword" },
				    "title": {
				      "type": "text",
				      "search_analyzer": "my_analyzer",
				      "fields": {
				        "keyword": { "type": "keyword" }
				      }
				    }
				  }
				}
				"""
		};
		var channel = new CatalogIndexChannel<HashDocument>(options);
		return channel;
	}

}
