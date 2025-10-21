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
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

public class ScriptedHashUpdateIngestionTests(IngestionCluster cluster, ITestOutputHelper output)
	: IntegrationTestBase(cluster, output)
{

	[Fact]
	public async Task EnsureDocumentsEndUpInIndex()
	{
		var indexPrefix = "update-data-";
		var slim = new CountdownEvent(1);
		var channel = CreateChannel(indexPrefix, slim);
		var bootstrapped = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure, "7-days-default");
		bootstrapped.Should().BeTrue("Expected to be able to bootstrap index channel");

		var indexName = channel.IndexName;
		var searchIndex = channel.Options.ActiveSearchAlias;

		// Verify the index does not exist yet
		var index = await Client.Indices.GetAsync(new GetIndexRequest(indexName));
		index.Indices.Should().BeNullOrEmpty();

		await WriteInitialDocuments(channel, slim, searchIndex, indexName, expectedVersion: 1);

		// Verify the index exists and has the correct settings
		index = await Client.Indices.GetAsync(new GetIndexRequest(indexName));
		index.Indices.Should().NotBeNullOrEmpty();
		index.Indices[indexName].Settings?.Index?.Lifecycle?.Name?.Should().NotBeNull().And.Be("7-days-default");

		// Write the same 100 documents again over the same channel, all documents should still have version 1.
		// Since no changes to hash occured all operations should have been a NOOP
		await WriteInitialDocuments(channel, slim, searchIndex, indexName, expectedVersion: 1);

		var refreshResult = await Client.Indices.RefreshAsync(indexName);
		refreshResult.IsValidResponse.Should().BeTrue("{0}", refreshResult.DebugInformation);

		// Now only update half the documents, assert the version is 2 for half of the documents
		await UpdateHalfOfDocuments(channel, slim, searchIndex, indexName, expectedVersion: 2);

		// when using a new Channel, it should pick up the existing index because we are using scripted upserts
		channel = CreateChannel(indexPrefix, slim);
		await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure, "7-days-default");
		channel.IndexName.Should().Be(indexName);
		// Now only update the same half the documents, assert the version is 3 for half of the documents and 1 for the untouched documents
		await UpdateHalfOfDocuments(channel, slim, searchIndex, indexName, expectedVersion: 3);

		// We can force a fresh new index to be created by disabling scripted hash updates
		channel = CreateChannel(indexPrefix, slim, useScriptedHashBulkUpsert: false);
		await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure, "7-days-default");
		channel.IndexName.Should().NotBe(indexName);
		indexName = channel.IndexName;
		// Now only update the same half the documents, assert the version is 3 for half of the documents and 1 for the untouched documents
		await UpdateHalfOfDocuments(channel, slim, searchIndex, indexName, expectedVersion: 1);

	}

	private async Task WriteInitialDocuments(CatalogIndexChannel<HashDocument> channel, CountdownEvent slim, string searchIndex, string indexName, int expectedVersion)
	{
		// Write 100 documents
		for (var i = 0; i < 100; i++)
			channel.TryWrite(new HashDocument { Title = "Hello World!", Id = $"hello-world-{i}" });
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"ecs document was not persisted within 10 seconds: {channel}");
		slim.Reset();
		await channel.RefreshAsync();
		await channel.ApplyAliasesAsync();

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

	private async Task UpdateHalfOfDocuments(CatalogIndexChannel<HashDocument> channel, CountdownEvent slim, string searchIndex, string indexName, int expectedVersion)
	{
		for (var i = 0; i < 100; i++)
		{
			var title = "Hello World!";
			if (i % 2 == 0)
				title += $"{i:N0}-{expectedVersion:N0}";
			channel.TryWrite(new HashDocument { Title = title , Id = $"hello-world-{i}" });
		}
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"Updates did not go through within 10s: {channel}");

		slim.Reset();
		await channel.RefreshAsync();
		await channel.ApplyAliasesAsync();
		var searchResult = await Client.SearchAsync<HashDocument>(s => s
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

		// validate only half were updated
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
				return new HashedBulkUpdate("hash", hash);
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
