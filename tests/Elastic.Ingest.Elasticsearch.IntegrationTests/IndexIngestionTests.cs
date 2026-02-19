// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Ingest.Elasticsearch.Indices;
using FluentAssertions;
using TUnit.Core;
using static System.Globalization.CultureInfo;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class IndexIngestionTests(IngestionCluster cluster)
	: IntegrationTestBase(cluster)
{
	[Test]
	public async Task EnsureDocumentsEndUpInIndex()
	{
		var indexPrefix = "catalog-data-";
		var slim = new CountdownEvent(1);
		var options = new IndexChannelOptions<CatalogDocument>(Client.Transport)
		{
			IndexFormat = indexPrefix + "{0:yyyy.MM.dd}",
			BulkOperationIdLookup = c => c.Id,
			BulkUpsertLookup = (c, id) => id == "hello-world-2",
			TimestampLookup = c => c.Created,
			BufferOptions = new BufferOptions
			{
				WaitHandle = slim, OutboundBufferMaxSize = 2,
			}
		};
		var channel = new IndexChannel<CatalogDocument>(options);
		var bootstrapped = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		bootstrapped.Should().BeTrue("Expected to be able to bootstrap index channel");

		var date = DateTimeOffset.Now;
		var indexName = string.Format(InvariantCulture, options.IndexFormat, date);

		var index = await Client.Indices.GetAsync(new GetIndexRequest(indexName));
		index.Indices.Should().BeNullOrEmpty();

		channel.TryWrite(new CatalogDocument { Created = date, Title = "Hello World!", Id = "hello-world" });
		channel.TryWrite(new CatalogDocument { Created = date, Title = "Hello World!", Id = "hello-world-2" });
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"ecs document was not persisted within 10 seconds: {channel}");

		var refreshResult = await Client.Indices.RefreshAsync(indexName);
		refreshResult.IsValidResponse.Should().BeTrue("{0}", refreshResult.DebugInformation);
		var searchResult = await Client.SearchAsync<CatalogDocument>(s => s.Indices(indexName));
		searchResult.Total.Should().Be(2);

		var storedDocument = searchResult.Documents.First();
		storedDocument.Id.Should().Be("hello-world");
		storedDocument.Title.Should().Be("Hello World!");

		var hit = searchResult.Hits.First();
		hit.Index.Should().Be(indexName);

		index = await Client.Indices.GetAsync(new GetIndexRequest(indexName));
		index.Indices.Should().NotBeNullOrEmpty();

		index.Indices[indexName].Settings?.Index?.Lifecycle?.Name?.Should().NotBeNull().And.Be("7-days-default");

		// Bug in client, for now assume template was applied because the ILM policy is set on the index.
		/*
		 // The JSON value could not be converted to Elastic.Clients.Elasticsearch.Names. Path: $.index_templates[0].index_template.index_patterns | LineNumber: 5 | BytePositionInLine: 28.

		var templateName = string.Format(options.IndexFormat, "template");
		var template = await Client.Indices.GetIndexTemplateAsync(new GetIndexTemplateRequest(templateName));
		template.IsValidResponse.Should().BeTrue("{0}", template.DebugInformation);
		template.IndexTemplates.First().Should().NotBeNull();
		template.IndexTemplates.First().Name.Should().Be(templateName);
		//template.IndexTemplates.First().IndexTemplate.Template..Should().Be(templateName);
		*/

	}
}
