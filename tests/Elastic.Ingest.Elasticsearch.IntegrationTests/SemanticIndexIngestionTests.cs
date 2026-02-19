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
using Elastic.Ingest.Elasticsearch.Semantic;
using Elastic.Ingest.Elasticsearch.Strategies;
using FluentAssertions;
using TUnit.Core;
using static System.Globalization.CultureInfo;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

/*
 * Tests: Semantic search ingestion via SemanticIndexChannel<SemanticArticle>
 *
 * Document type: SemanticArticle (Elastic.Mapping)
 *   [Id][Keyword]                     id              — bulk operation routing
 *   [Text]                            title           — analyzer: "semantic_content" (standard + lowercase + asciifolding)
 *   [SemanticText(InferenceId=...)]   semantic_text   — ELSER inference via semantic_text field type
 *   [Date]                            created         — document timestamp
 *
 * Mappings & analysis:
 *   TestMappingContext.SemanticArticle.Context
 *     ├── GetMappingsJson()      → field types + analyzer refs + semantic_text with inference_id
 *     └── ConfigureAnalysis      → semantic_content analyzer (standard + lowercase + asciifolding)
 *   IngestStrategies.Index<>()
 *     └── GetMappingSettings     → merged entity settings + analysis JSON
 *
 * Bootstrap (SemanticIndexChannel):
 *   BootstrapElasticsearchAsync(Failure)
 *   ├── InferenceEndpointStep  "test-elser-inference"          (index inference, 1 thread)
 *   ├── InferenceEndpointStep  "test-search-elser-inference"   (search inference, 2 threads)
 *   ├── ComponentTemplate      semantic-data-template          (settings + analysis)
 *   ├── ComponentTemplate      semantic-data-template          (mappings + semantic_text)
 *   └── IndexTemplate          semantic-data-template          (pattern: semantic-data-*)
 *
 * Index naming:
 *   Format:  semantic-data-{yyyy.MM.dd}
 *
 * Ingestion flow:
 *   ┌─────────────────────────────────────────────────────────────┐
 *   │ 1. Bootstrap creates inference endpoints (ELSER)            │
 *   │ 2. Bootstrap creates component + index templates            │
 *   │ 3. Write 2 docs via channel                                 │
 *   │ 4. Refresh & search → verify both docs landed               │
 *   │ 5. Verify semantic_content analyzer in index settings       │
 *   └─────────────────────────────────────────────────────────────┘
 */
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class SemanticIndexIngestionTests(IngestionCluster cluster)
	: IntegrationTestBase(cluster)
{
	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync("semantic-data");

	[After(Test)]
	public async Task Teardown() => await CleanupPrefixAsync("semantic-data");

	[Test]
	public async Task EnsureDocumentsEndUpInSemanticIndex()
	{
		var ctx = TestMappingContext.SemanticArticle.Context;
		var strategy = IngestStrategies.Index<SemanticArticle>(ctx);

		var indexPrefix = "semantic-data-";
		var slim = new CountdownEvent(1);

		var options = new SemanticIndexChannelOptions<SemanticArticle>(Client.Transport)
		{
			IndexFormat = indexPrefix + "{0:yyyy.MM.dd}",
			BulkOperationIdLookup = c => c.Id,
			BulkUpsertLookup = (c, id) => id == "hello-world-2",
			TimestampLookup = c => c.Created,
			InferenceId = "test-elser-inference",
			SearchInferenceId = "test-search-elser-inference",
			UsePreexistingInferenceIds = false,
			SearchNumThreads = 2,
			IndexNumThreads = 1,
			BufferOptions = new BufferOptions
			{
				WaitHandle = slim, OutboundBufferMaxSize = 2,
			},
			GetMapping = (_, _) => ctx.GetMappingsJson!(),
			GetMappingSettings = (_, _) => strategy.GetMappingSettings!()
		};

		var channel = new SemanticIndexChannel<SemanticArticle>(options);
		var bootstrapped = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		bootstrapped.Should().BeTrue("Expected to be able to bootstrap semantic index channel");

		channel.InferenceId.Should().Be("test-elser-inference");
		channel.SearchInferenceId.Should().Be("test-search-elser-inference");

		var date = DateTimeOffset.Now;
		var indexName = string.Format(InvariantCulture, options.IndexFormat, date);

		var index = await Client.Indices.GetAsync(new GetIndexRequest(indexName));
		index.Indices.Should().BeNullOrEmpty();

		channel.TryWrite(new SemanticArticle { Created = date, Title = "Hello World!", Id = "hello-world" });
		channel.TryWrite(new SemanticArticle { Created = date, Title = "Semantic search is powerful", Id = "hello-world-2" });
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"semantic documents were not persisted within 10 seconds: {channel}");

		var refreshResult = await Client.Indices.RefreshAsync(indexName);
		refreshResult.IsValidResponse.Should().BeTrue("{0}", refreshResult.DebugInformation);
		var searchResult = await Client.SearchAsync<SemanticArticle>(s => s.Indices(indexName));
		searchResult.Total.Should().Be(2);

		var storedDocument = searchResult.Documents.First();
		storedDocument.Id.Should().Be("hello-world");
		storedDocument.Title.Should().Be("Hello World!");

		var hit = searchResult.Hits.First();
		hit.Index.Should().Be(indexName);

		index = await Client.Indices.GetAsync(new GetIndexRequest(indexName));
		index.Indices.Should().NotBeNullOrEmpty();

		var lifecycle = index.Indices[indexName].Settings?.Index?.Lifecycle;
		if (lifecycle?.Name is not null)
			lifecycle.Name.Should().Be("7-days-default");

		var indexSettings = index.Indices[indexName].Settings;
		indexSettings.Should().NotBeNull();
		indexSettings.Analysis?.Analyzers.Should().ContainKey("semantic_content");
	}
}
