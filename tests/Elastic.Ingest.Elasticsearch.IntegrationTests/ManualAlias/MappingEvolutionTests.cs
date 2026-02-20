// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.ManualAlias;

/*
 * Use case: Manual alias management  (https://elastic.github.io/elastic-ingest-dotnet/index-management/rollover/manual-alias)
 * Tests:    Mapping evolution — new date-stamped indices pick up updated templates
 *
 * Scenario:
 *   ┌──────────────────────────────────────────────────────────────────────────┐
 *   │  1. Bootstrap V1 (ProductCatalogConfig, Catalog variant)                 │
 *   │     → creates cat-products-template with hash₁                           │
 *   │     → writes doc "AEVO-001" to cat-products-<TIMESTAMP₁>                │
 *   │     → apply aliases: cat-products-latest → TIMESTAMP₁                    │
 *   │                      cat-products-search → TIMESTAMP₁                    │
 *   │                                                                          │
 *   │  2. Bootstrap V2 (ProductCatalogV2Config, CatalogV2 variant)             │
 *   │     → hash₂ ≠ hash₁ → templates are updated                             │
 *   │     → writes doc "AEVO-002" to cat-products-<TIMESTAMP₂> (new index!)   │
 *   │     → apply aliases: cat-products-latest → TIMESTAMP₂                    │
 *   │                      cat-products-search → TIMESTAMP₂                    │
 *   │                                                                          │
 *   │  3. Verify:                                                              │
 *   │     • TIMESTAMP₁ has V1 analysis (no stop filter)                        │
 *   │     • TIMESTAMP₂ has V2 analysis (stop filter present)                   │
 *   │     • Search via cat-products-search finds AEVO-002                      │
 *   └──────────────────────────────────────────────────────────────────────────┘
 *
 * Key insight for alias users:
 *   Each new date-stamped index is created from the current templates,
 *   so mapping evolution happens naturally without downtime or reindexing.
 */
[NotInParallel("manual-alias")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class MappingEvolutionTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string Prefix = "cat-products";
	private const string ReadAlias = "cat-products-search";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync(Prefix);

	[After(Test)]
	public async Task Teardown() => await CleanupPrefixAsync(Prefix);

	[Test]
	public async Task NewDateStampedIndexPicksUpV2Templates()
	{
		// ── Phase 1: Bootstrap V1 + write + alias ───────────────────────

		var ctxV1 = TestMappingContext.ProductCatalogCatalog.Context;
		var slim = new CountdownEvent(1);
		var channelV1 = new IngestChannel<ProductCatalog>(
			new IngestChannelOptions<ProductCatalog>(Transport, ctxV1)
			{
				BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 1 }
			});

		(await channelV1.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();
		var hashV1 = channelV1.ChannelHash;

		channelV1.TryWrite(new ProductCatalog
		{
			Sku = "AEVO-001", Name = "Alias Evo V1", Description = "First version",
			Category = "Widgets", Price = 10.0, Tags = ["v1"]
		});
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("V1 write timed out");

		var concreteV1 = await ResolveConcreteIndexAsync();
		concreteV1.Should().NotBeNull("a concrete index should exist after writing");

		await Transport.RequestAsync<StringResponse>(HttpMethod.POST, $"/{concreteV1}/_refresh");
		await channelV1.ApplyAliasesAsync(concreteV1!);

		// Verify V1 index has data
		var searchV1 = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{concreteV1}/_search");
		searchV1.Body.Should().Contain("\"AEVO-001\"");

		// Small delay so the next date-stamped index gets a different name
		await Task.Delay(TimeSpan.FromSeconds(1));

		// ── Phase 2: Bootstrap V2 + write + alias ───────────────────────

		var ctxV2 = TestMappingContext.ProductCatalogV2Catalog.Context;
		slim.Reset();
		var channelV2 = new IngestChannel<ProductCatalogV2>(
			new IngestChannelOptions<ProductCatalogV2>(Transport, ctxV2)
			{
				BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 1 }
			});

		(await channelV2.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();
		var hashV2 = channelV2.ChannelHash;

		hashV2.Should().NotBe(hashV1,
			"V2 has different fields + analysis so hash must differ");

		channelV2.TryWrite(new ProductCatalogV2
		{
			Sku = "AEVO-002", Name = "Alias Evo V2", Description = "Second version",
			Category = "Widgets", Price = 5.0, Tags = ["v2"], IsFeatured = true
		});
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("V2 write timed out");

		var concreteV2 = await ResolveLatestConcreteIndexAsync(concreteV1!);
		concreteV2.Should().NotBeNull("a second concrete index should exist");
		concreteV2.Should().NotBe(concreteV1, "V2 should create a new date-stamped index");

		await Transport.RequestAsync<StringResponse>(HttpMethod.POST, $"/{concreteV2}/_refresh");
		await channelV2.ApplyAliasesAsync(concreteV2!);

		// ── Phase 3: Verify analysis difference ─────────────────────────

		var settingsV1 = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{concreteV1}/_settings");
		settingsV1.Body.Should().NotContain("\"stop\"",
			"V1 index should not have the stop filter");

		var settingsV2 = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{concreteV2}/_settings");
		settingsV2.Body.Should().Contain("\"stop\"",
			"V2 index should have the stop filter from V2 analysis");

		// Verify the read alias now points to the V2 index
		var searchViaAlias = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{ReadAlias}/_search");
		searchViaAlias.ApiCallDetails.HttpStatusCode.Should().Be(200);
		searchViaAlias.Body.Should().Contain("\"AEVO-002\"");
	}

	private async Task<string?> ResolveConcreteIndexAsync()
	{
		var resolve = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_resolve/index/{Prefix}-*");
		if (!resolve.ApiCallDetails.HasSuccessfulStatusCode) return null;

		using var doc = JsonDocument.Parse(resolve.Body);
		return doc.RootElement
			.GetProperty("indices")
			.EnumerateArray()
			.Select(e => e.GetProperty("name").GetString())
			.FirstOrDefault(n => !string.IsNullOrEmpty(n));
	}

	private async Task<string?> ResolveLatestConcreteIndexAsync(string excludeIndex)
	{
		var resolve = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_resolve/index/{Prefix}-*");
		if (!resolve.ApiCallDetails.HasSuccessfulStatusCode) return null;

		using var doc = JsonDocument.Parse(resolve.Body);
		return doc.RootElement
			.GetProperty("indices")
			.EnumerateArray()
			.Select(e => e.GetProperty("name").GetString())
			.Where(n => !string.IsNullOrEmpty(n) && n != excludeIndex)
			.FirstOrDefault();
	}
}
