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

public class ChannelHashTests(IngestionCluster cluster, ITestOutputHelper output)
	: IntegrationTestBase(cluster, output)
{

	[Fact]
	public async Task HashChangesWithIndexChannelShouldCreateNewIndex()
	{
		var indexPrefix = "hash-data-";
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

		// when using a new Channel, it should pick up the existing index because we are using scripted upserts
		channel = CreateChannel(indexPrefix, slim);
		await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure, "7-days-default");
		channel.IndexName.Should().Be(indexName);

		// If we change settings we should create a new index
		channel = CreateChannel(indexPrefix, slim, changeSettings: true);
		await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure, "7-days-default");
		channel.IndexName.Should().NotBe(indexName);

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

	private CatalogIndexChannel<HashDocument> CreateChannel(string indexPrefix, CountdownEvent slim, bool changeSettings = false)
	{
		var options = new CatalogIndexChannelOptions<HashDocument>(Client.Transport)
		{
			IndexFormat = indexPrefix + "{0:yyyy.MM.dd.HH-mm-ss-fffffff}",
			ScriptedHashBulkUpsertLookup = (c, channelHash) =>
			{
				var hash = HashedBulkUpdate.CreateHash(channelHash, c.Id, c.Title ?? string.Empty);
				c.Hash = hash;
				return new HashedBulkUpdate("hash", hash);
			},
			ActiveSearchAlias = indexPrefix + "search",
			BulkOperationIdLookup = c => c.Id,
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
				$$"""
				{
				  "properties": {
					"hash": { "type": "{{(changeSettings ? "keyword" : "text")}}" },
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
