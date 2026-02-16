// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.MultiChannel;
using Elastic.Transport;

// ─── Create transport ───────────────────────────────────────────────────────
// Point to a local or cloud Elasticsearch instance
var transport = new DistributedTransport(new TransportConfiguration(new Uri("http://localhost:9200")));

// ─── Create orchestrator from source-generated mapping contexts ─────────────
// The orchestrator auto-configures two channels (lexical + semantic) and
// determines whether to Multiplex or Reindex based on template hash comparison.
using var orchestrator = new IncrementalSyncOrchestrator<KnowledgeArticle>(
	transport,
	primary: ExampleMappingContext.KnowledgeArticle.Context,
	secondary: ExampleMappingContext.KnowledgeArticleSemantic.Context
);

// ─── Start: bootstrap + strategy selection ──────────────────────────────────
var strategy = await orchestrator.StartAsync(BootstrapMethod.Failure);
Console.WriteLine($"Strategy: {strategy}");

// ─── Write some documents ───────────────────────────────────────────────────
var articles = new[]
{
	new KnowledgeArticle
	{
		Url = "https://example.com/getting-started",
		Title = "Getting Started with Elasticsearch",
		Body = "Elasticsearch is a distributed search and analytics engine...",
		Hash = "abc123",
		UpdatedAt = DateTimeOffset.UtcNow
	},
	new KnowledgeArticle
	{
		Url = "https://example.com/ingest-dotnet",
		Title = "Using the .NET Ingest Library",
		Body = "The Elastic.Ingest.Elasticsearch library provides buffered channels...",
		Hash = "def456",
		UpdatedAt = DateTimeOffset.UtcNow
	}
};

foreach (var article in articles)
	orchestrator.TryWrite(article);

// ─── Complete: drain → reindex/multiplex → cleanup → aliases ────────────────
var completed = await orchestrator.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(10));
Console.WriteLine($"Complete: {(completed ? "OK" : "FAILED")}");

Console.WriteLine("Done.");
