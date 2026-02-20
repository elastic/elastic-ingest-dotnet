// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.SingleIndex;

/*
 * Use case: Single index  (https://elastic.github.io/elastic-ingest-dotnet/index-management/single-index)
 * Tests:    Mapping evolution — what happens when templates change for a fixed-name index
 *
 * Scenario:
 *   ┌──────────────────────────────────────────────────────────────────────┐
 *   │  1. Bootstrap V1 (ProductCatalogConfig)                              │
 *   │     → edge_ngram min=2/max=15, price_tier runtime field              │
 *   │     → creates idx-products-template with hash₁                       │
 *   │                                                                      │
 *   │  2. Write doc "EVO-001" → creates idx-products index with V1 schema  │
 *   │                                                                      │
 *   │  3. Bootstrap V2 (ProductCatalogV2Config)                            │
 *   │     → edge_ngram min=3/max=20, discount_eligible runtime field       │
 *   │     → hash₂ ≠ hash₁ → templates are updated                         │
 *   │                                                                      │
 *   │  4. Existing idx-products index STILL has V1 mappings                │
 *   │     (templates only apply when indices are first created)            │
 *   │                                                                      │
 *   │  5. Delete the index + write "EVO-002"                               │
 *   │     → idx-products re-created with V2 template → V2 analysis active  │
 *   └──────────────────────────────────────────────────────────────────────┘
 *
 * Key insight for single-index users:
 *   Template changes are NOT applied retroactively. You must recreate the
 *   index (delete + re-ingest) or use the Update Mapping API.
 *   For zero-downtime updates, use manual alias management instead.
 */
[NotInParallel("single-index")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class MappingEvolutionTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string Prefix = "idx-products";
	private const string IndexName = "idx-products";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync(Prefix);

	[After(Test)]
	public async Task Teardown() => await CleanupPrefixAsync(Prefix);

	[Test]
	public async Task TemplateUpdateDetectedAndNewIndexPicksUpV2Mappings()
	{
		// ── Phase 1: Bootstrap V1 and write a document ──────────────────

		var ctxV1 = TestMappingContext.ProductCatalog.Context;
		var slim = new CountdownEvent(1);
		var optionsV1 = new IngestChannelOptions<ProductCatalog>(Transport, ctxV1)
		{
			BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 1 }
		};
		var channelV1 = new IngestChannel<ProductCatalog>(optionsV1);

		(await channelV1.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();
		var hashV1 = channelV1.ChannelHash;
		hashV1.Should().NotBeNullOrEmpty();

		channelV1.TryWrite(new ProductCatalog
		{
			Sku = "EVO-001", Name = "Evolution Widget V1", Description = "Initial version",
			Category = "Widgets", Price = 15.0, Tags = ["v1"]
		});
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("V1 write timed out");

		await Transport.RequestAsync<StringResponse>(HttpMethod.POST, $"/{IndexName}/_refresh");

		// Verify V1 template has the V1 analysis (edge_ngram min=2)
		var settingsV1 = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_component_template/{Prefix}-template-mappings");
		settingsV1.ApiCallDetails.HttpStatusCode.Should().Be(200);
		settingsV1.Body.Should().Contain("\"product_autocomplete\"");

		// ── Phase 2: Bootstrap V2 — hash should change ──────────────────

		var ctxV2 = TestMappingContext.ProductCatalogV2.Context;
		var optionsV2 = new IngestChannelOptions<ProductCatalogV2>(Transport, ctxV2)
		{
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};
		var channelV2 = new IngestChannel<ProductCatalogV2>(optionsV2);

		(await channelV2.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();
		var hashV2 = channelV2.ChannelHash;

		hashV2.Should().NotBe(hashV1,
			"V2 config has different analysis (edge_ngram 3..20 + stop filter) so hash must differ");

		// ── Phase 3: Existing index still has V1 settings ───────────────

		var indexSettings = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{IndexName}/_settings");
		indexSettings.ApiCallDetails.HttpStatusCode.Should().Be(200);

		// The template was updated, but the existing index keeps its original settings.
		// Verify the original data is still there.
		var searchV1 = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{IndexName}/_search");
		searchV1.Body.Should().Contain("\"EVO-001\"",
			"V1 data should still be present in the existing index");

		// ── Phase 4: Verify V2 templates have updated content ──────────

		// Verify the component templates and index template were updated to V2.
		var mappingsTemplateV2 = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_component_template/{Prefix}-template-mappings");
		mappingsTemplateV2.ApiCallDetails.HttpStatusCode.Should().Be(200);
		mappingsTemplateV2.Body.Should().Contain("\"stop\"",
			"V2 component template should contain the stop filter from V2 analysis");

		// Verify the index template's _meta.hash was updated to V2
		var indexTemplate = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_index_template/{Prefix}-template");
		indexTemplate.ApiCallDetails.HttpStatusCode.Should().Be(200);
		indexTemplate.Body.Should().Contain(hashV2,
			"index template _meta.hash should reflect the V2 channel hash");

		// ── Phase 5: Delete index, re-create, write new data ────────────

		await Transport.RequestAsync<StringResponse>(
			HttpMethod.DELETE, $"/{IndexName}?ignore_unavailable=true");

		slim.Reset();
		var channelV2Write = new IngestChannel<ProductCatalogV2>(
			new IngestChannelOptions<ProductCatalogV2>(Transport, ctxV2)
			{
				BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 1 }
			});
		await channelV2Write.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

		channelV2Write.TryWrite(new ProductCatalogV2
		{
			Sku = "EVO-002", Name = "Evolution Widget V2", Description = "Updated version",
			Category = "Widgets", Price = 8.0, Tags = ["v2"], IsFeatured = true
		});
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("V2 write timed out");

		await Transport.RequestAsync<StringResponse>(HttpMethod.POST, $"/{IndexName}/_refresh");

		var searchV2 = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{IndexName}/_search");
		searchV2.Body.Should().Contain("\"EVO-002\"");
		searchV2.Body.Should().NotContain("\"EVO-001\"",
			"old data was deleted with the index");
	}
}
