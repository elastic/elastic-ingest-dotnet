// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Security.Cryptography;
using System.Text;
using AutoBogus;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.Enrichment;
using Elastic.Ingest.Elasticsearch.Helpers;
using Elastic.Ingest.IncrementalSync.Example;
using Elastic.Mapping;
using Elastic.Transport;
using Microsoft.Extensions.Configuration;

// ─── Configuration ──────────────────────────────────────────────────────────
var config = new ConfigurationBuilder()
	.AddUserSecrets(typeof(RecipeMappingContext).Assembly, optional: true)
	.Build();

var url = config["Parameters:ElasticsearchUrl"] ?? "http://localhost:9200";
var apiKey = config["Parameters:ElasticsearchApiKey"];

Console.WriteLine($"Elasticsearch URL : {url}");
Console.WriteLine($"API key configured: {!string.IsNullOrEmpty(apiKey)}");

// ─── Transport ──────────────────────────────────────────────────────────────
var transportConfig = new TransportConfiguration(new Uri(url))
{
	Authentication = string.IsNullOrWhiteSpace(apiKey) ? null : new ApiKey(apiKey)
};
var transport = new DistributedTransport(transportConfig);

// ─── Resolve environment-aware contexts via CreateContext ────────────────────
var env = ElasticsearchTypeContext.ResolveDefaultNamespace();
Console.WriteLine($"Environment       : {env}");

var primaryContext = RecipeMappingContext.RecipeDocument.CreateContext(env: env);
var secondaryContext = RecipeMappingContext.RecipeDocumentSemantic.CreateContext(env: env);

Console.WriteLine($"Primary target    : {primaryContext.IndexStrategy!.WriteTarget}");
Console.WriteLine($"Secondary target  : {secondaryContext.IndexStrategy!.WriteTarget}");

Console.WriteLine($"AI enrichment     : {(primaryContext.AiEnrichmentProvider != null ? "configured" : "none")}");
Console.WriteLine();

// ─── Channel diagnostics ─────────────────────────────────────────────────────
void ConfigureChannel(string label, IngestChannelOptions<RecipeDocument> opts)
{
	opts.ExportResponseCallback = (response, buffer) =>
	{
		var status = response.ApiCallDetails.HttpStatusCode;
		var items = response.Items?.Count ?? 0;
		var errors = response.Items?.Count(i => i.Status >= 400) ?? 0;
		Console.WriteLine($"  [{label}] Bulk response: HTTP {status}, {items} items, {errors} errors, buffer count: {buffer.Count}");
		if (response.ApiCallDetails.HasSuccessfulStatusCode) return;
		Console.WriteLine($"  [{label}] DebugInfo: {response.ApiCallDetails.DebugInformation}");
	};
	opts.ExportExceptionCallback = ex =>
		Console.WriteLine($"  [{label}] Export EXCEPTION: {ex.Message}");
	opts.ExportBufferCallback = () =>
		Console.WriteLine($"  [{label}] Buffer flushed");
	opts.ExportMaxRetriesCallback = failed =>
		Console.WriteLine($"  [{label}] Max retries exceeded for {failed.Count} items");
}

// ─── Orchestrator ───────────────────────────────────────────────────────────
using var orchestrator = new IncrementalSyncOrchestrator<RecipeDocument>(
	transport,
	primary: primaryContext,
	secondary: secondaryContext
)
{
	ConfigurePrimary = opts => ConfigureChannel("primary", opts),
	ConfigureSecondary = opts => ConfigureChannel("secondary", opts),
	OnReindexProgress = (label, p) =>
		Console.WriteLine($"  [{label}] total={p.Total} created={p.Created} updated={p.Updated} deleted={p.Deleted} noops={p.Noops} completed={p.IsCompleted}{(p.Error != null ? $" ERROR={p.Error}" : "")}"),
	OnDeleteByQueryProgress = (label, p) =>
		Console.WriteLine($"  [{label}] total={p.Total} deleted={p.Deleted} completed={p.IsCompleted}{(p.Error != null ? $" ERROR={p.Error}" : "")}"),
};

using var aiOrchestrator = new AiEnrichmentOrchestrator(transport, primaryContext);

orchestrator.AddPreBootstrapTask(async (_, ct) =>
{
	Console.WriteLine("Pre-bootstrap: initializing AI enrichment infrastructure...");
	await aiOrchestrator.InitializeAsync(ct);
});

// ─── Start ──────────────────────────────────────────────────────────────────
Console.WriteLine("Starting orchestrator...");
var context = await orchestrator.StartAsync(BootstrapMethod.Failure);
Console.WriteLine($"Strategy          : {context.Strategy}");

// ─── Generate and write fake recipes ────────────────────────────────────────
var faker = new AutoFaker<RecipeDocument>()
	.UseSeed(1337)
	.RuleFor(r => r.Slug, f => $"{f.Lorem.Slug()}-{f.IndexFaker}")
	.RuleFor(r => r.Title, f => f.Lorem.Sentence(4))
	.RuleFor(r => r.Description, f => f.Lorem.Paragraph())
	.RuleFor(r => r.Cuisine, f => f.PickRandom("Italian", "Mexican", "Japanese", "Indian", "French", "Thai", "Greek", "Ethiopian"))
	.RuleFor(r => r.Ingredients, f => Enumerable.Range(0, f.Random.Int(3, 10)).Select(_ => f.Commerce.ProductName()).ToArray())
	.RuleFor(r => r.PrepTimeMinutes, f => f.Random.Int(5, 60))
	.RuleFor(r => r.CookTimeMinutes, f => f.Random.Int(10, 120))
	.RuleFor(r => r.Servings, f => f.Random.Int(1, 8))
	.RuleFor(r => r.ContentHash, (_, r) => ComputeHash($"{r.Title}{r.Description}"))
	.RuleFor(r => r.AiSummary, () => null)
	.RuleFor(r => r.AiTags, () => null);

var recipes = faker.Generate(1000);
Console.WriteLine($"Writing {recipes.Count} recipes...");

var written = 0;
var dropped = 0;
foreach (var recipe in recipes)
{
	if (orchestrator.TryWrite(recipe))
		written++;
	else
		dropped++;
}
Console.WriteLine($"Write results     : {written} accepted, {dropped} dropped");

// ─── Complete ───────────────────────────────────────────────────────────────
var completed = await orchestrator.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(30));
Console.WriteLine($"Orchestrator done : {(completed ? "OK" : "FAILED")}");

// ─── Validate: document counts after indexing ────────────────────────────────
Console.WriteLine();
Console.WriteLine("── Validation ─────────────────────────────────────────────────────────────");
var primaryAlias = primaryContext.ResolveWriteAlias();
var secondaryAlias = secondaryContext.ResolveWriteAlias();

Console.WriteLine($"Primary index     : {context.PrimaryWriteAlias}");
Console.WriteLine($"Secondary index   : {context.SecondaryWriteAlias}");

var primaryCount = await CountAsync(transport, primaryAlias);
Console.WriteLine($"Primary count     : {primaryCount}");

var secondaryCount = await CountAsync(transport, secondaryAlias);
Console.WriteLine($"Secondary count   : {secondaryCount}");

// ─── Post-indexing AI enrichment ────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine($"Running AI enrichment against: {secondaryAlias}");

AiEnrichmentProgress? lastProgress = null;
await foreach (var p in aiOrchestrator.EnrichAsync(secondaryAlias, new AiEnrichmentOptions { MaxEnrichmentsPerRun = 200 }))
{
	Console.WriteLine($"  [ai] {p.Phase}: enriched={p.Enriched} failed={p.Failed} candidates={p.TotalCandidates}{(p.Message != null ? $" — {p.Message}" : "")}");
	lastProgress = p;
}
if (lastProgress != null)
	Console.WriteLine($"AI enrichment     : {lastProgress.Enriched} enriched, {lastProgress.Failed} failed ({lastProgress.TotalCandidates} candidates)");

// ─── Validate: counts after enrichment ──────────────────────────────────────
await transport.PostAsync<StringResponse>($"{secondaryAlias}/_refresh", PostData.Empty);

var lookupIndexName = $"{primaryContext.IndexStrategy!.WriteTarget}-ai-cache";
var lookupCount = await CountAsync(transport, lookupIndexName);
Console.WriteLine($"AI lookup count   : {lookupCount}");

var enrichedCount = await CountAsync(transport, secondaryAlias, """{"exists":{"field":"ai_summary"}}""");
Console.WriteLine($"Enriched docs     : {enrichedCount} / {secondaryCount}");

Console.WriteLine();
Console.WriteLine("Done.");

static async Task<long> CountAsync(ITransport transport, string index, string? query = null)
{
	var body = query != null ? $"{{\"query\":{query}}}" : null;
	var response = await transport.RequestAsync<StringResponse>(
		Elastic.Transport.HttpMethod.POST, $"{index}/_count",
		body != null ? PostData.String(body) : PostData.Empty).ConfigureAwait(false);

	if (response.ApiCallDetails.HttpStatusCode is not 200)
		return -1;

	using var doc = System.Text.Json.JsonDocument.Parse(response.Body);
	return doc.RootElement.TryGetProperty("count", out var count) ? count.GetInt64() : -1;
}

static string ComputeHash(string input)
{
	var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
	return Convert.ToHexString(hash)[..16].ToLowerInvariant();
}
