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
using Elastic.Transport.Extensions;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

public class SemanticIndexIngestionTests(IngestionCluster cluster, ITestOutputHelper output)
	: IntegrationTestBase(cluster, output)
{
	[Fact]
	public async Task EnsureDocumentsEndUpInSemanticIndex()
	{
		var indexPrefix = "semantic-data-";
		var slim = new CountdownEvent(1);

		var options = new SemanticIndexChannelOptions<CatalogDocument>(Client.Transport)
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

			// Semantic mapping for ELSER sparse vector embeddings
			GetMapping = (inferenceId, searchInferenceId) =>
				$$"""
				{
				  "properties": {
				    "title": {
				      "type": "text",
				      "analyzer": "semantic_analyzer",
				      "fields": {
				        "keyword": { "type": "keyword" }
				      }
				    },
				    "title_embedding": {
				      "type": "sparse_vector"
				    },
				    "semantic_text": {
				      "type": "semantic_text",
				      "inference_id": "{{inferenceId}}"
				    }
				  }
				}
				""",

			GetMappingSetting = (inferenceId, searchInferenceId) =>
				"""
				{
				  "analysis": {
				    "analyzer": {
				      "semantic_analyzer": {
				        "type": "custom",
				        "tokenizer": "standard",
				        "filter": [ "lowercase", "asciifolding" ]
				      }
				    }
				  }
				}
				"""
		};

		var channel = new SemanticIndexChannel<CatalogDocument>(options);
		var bootstrapped = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure, "7-days-default");
		bootstrapped.Should().BeTrue("Expected to be able to bootstrap semantic index channel");

		// Verify inference endpoints were created
		channel.InferenceId.Should().Be("test-elser-inference");
		channel.SearchInferenceId.Should().Be("test-search-elser-inference");

		var date = DateTimeOffset.Now;
		var indexName = string.Format(options.IndexFormat, date);

		var index = await Client.Indices.GetAsync(new GetIndexRequest(indexName));
		index.Indices.Should().BeNullOrEmpty();

		channel.TryWrite(new CatalogDocument { Created = date, Title = "Hello World!", Id = "hello-world" });
		channel.TryWrite(new CatalogDocument { Created = date, Title = "Semantic search is powerful", Id = "hello-world-2" });
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"semantic documents were not persisted within 10 seconds: {channel}");

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

		// Verify the custom analyzer is configured in the index settings
		var indexSettings = index.Indices[indexName].Settings;
		indexSettings.Should().NotBeNull();

		// Check that the analysis settings contain our custom analyzer
		var analysisSettings = indexSettings?.Analysis;
		analysisSettings.Should().NotBeNull();
		analysisSettings?.Analyzers.Should().ContainKey("semantic_analyzer");

		var semanticAnalyzer = analysisSettings?.Analyzers["semantic_analyzer"];
		semanticAnalyzer.Should().NotBeNull();
		// Verify the analyzer configuration matches what we defined in GetMappingSetting
		semanticAnalyzer?.Type.Should().Be("custom");
		semanticAnalyzer?.Tokenizer.Should().Be("standard");
		semanticAnalyzer?.Filter.Should().Contain("lowercase").And.Contain("asciifolding");

		// Verify inference endpoints exist
		var inferenceResponse = await Client.Transport.GetAsync<Elastic.Transport.VoidResponse>($"_inference/sparse_embedding/{channel.InferenceId}");
		inferenceResponse.ApiCallDetails.HttpStatusCode.Should().Be(200, "Index inference endpoint should exist");

		var searchInferenceResponse = await Client.Transport.GetAsync<Elastic.Transport.VoidResponse>($"_inference/sparse_embedding/{channel.SearchInferenceId}");
		searchInferenceResponse.ApiCallDetails.HttpStatusCode.Should().Be(200, "Search inference endpoint should exist");
	}

	[Fact]
	public async Task EnsureDocumentsEndUpInSemanticIndexWithPreexistingInference()
	{
		var indexPrefix = "semantic-preexisting-";
		var slim = new CountdownEvent(1);

		// First create the inference endpoints manually to simulate preexisting ones
		var inferenceId = "preexisting-elser-inference";
		var searchInferenceId = "preexisting-search-elser-inference";

		// Create inference endpoints
		await CreateElserInferenceAsync(inferenceId, 1);
		await CreateElserInferenceAsync(searchInferenceId, 2);

		var options = new SemanticIndexChannelOptions<CatalogDocument>(Client.Transport)
		{
			IndexFormat = indexPrefix + "{0:yyyy.MM.dd}",
			BulkOperationIdLookup = c => c.Id,
			TimestampLookup = c => c.Created,
			InferenceId = inferenceId,
			SearchInferenceId = searchInferenceId,
			UsePreexistingInferenceIds = true,
			BufferOptions = new BufferOptions
			{
				WaitHandle = slim, OutboundBufferMaxSize = 1,
			}
		};

		var channel = new SemanticIndexChannel<CatalogDocument>(options);
		var bootstrapped = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure, "7-days-default");
		bootstrapped.Should().BeTrue("Expected to be able to bootstrap semantic index channel with preexisting inference endpoints");

		var date = DateTimeOffset.Now;
		var indexName = string.Format(options.IndexFormat, date);

		channel.TryWrite(new CatalogDocument { Created = date, Title = "Preexisting inference test", Id = "preexisting-test" });
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"semantic document was not persisted within 10 seconds: {channel}");

		var refreshResult = await Client.Indices.RefreshAsync(indexName);
		refreshResult.IsValidResponse.Should().BeTrue("{0}", refreshResult.DebugInformation);
		var searchResult = await Client.SearchAsync<CatalogDocument>(s => s.Indices(indexName));
		searchResult.Total.Should().Be(1);

		var storedDocument = searchResult.Documents.First();
		storedDocument.Id.Should().Be("preexisting-test");
		storedDocument.Title.Should().Be("Preexisting inference test");
	}

	private async Task CreateElserInferenceAsync(string inferenceId, int numThreads)
	{
		var data = $$"""
			{
			  "service": "elser",
			  "service_settings": {
			    "adaptive_allocations": {
			      "enabled": true,
			      "min_number_of_allocations": 3,
			      "max_number_of_allocations": 10
			    },
			    "num_threads": {{numThreads:N0}}
			  }
			}
			""";

		await Client.Transport.PutAsync<Elastic.Transport.StringResponse>(
			$"_inference/sparse_embedding/{inferenceId}",
			Elastic.Transport.PostData.String(data));
	}
}
