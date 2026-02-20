// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Legacy;

/*
 * Tests: OperationMode.Create vs OperationMode.Index on IndexChannel
 *
 * Source: src/Elastic.Ingest.Elasticsearch/Indices/OperationMode.cs
 *         (used by BulkRequestDataFactory.CreateBulkOperationHeaderForIndex)
 *
 * OperationMode.Index (default):
 *   Bulk header = { "index": { "_index": "...", "_id": "..." } }
 *   Overwrites existing docs with the same _id.
 *
 * OperationMode.Create:
 *   Bulk header = { "create": { "_index": "...", "_id": "..." } }
 *   Returns 409 (version_conflict) if a doc with the same _id exists.
 *
 *   ┌──────────────────────────────────────────────────────────────────┐
 *   │  Test 1 — OperationMode.Index:                                   │
 *   │    Write doc SKU="OM-001", price=10 → created                    │
 *   │    Write doc SKU="OM-001", price=99 → overwrites (no error)      │
 *   │    _search → 1 doc, price=99                                     │
 *   │                                                                   │
 *   │  Test 2 — OperationMode.Create:                                   │
 *   │    Write doc SKU="OM-001", price=10 → created                    │
 *   │    Write doc SKU="OM-001", price=99 → 409 conflict (noop)        │
 *   │    _search → 1 doc, price=10 (original preserved)                │
 *   └──────────────────────────────────────────────────────────────────┘
 */
[NotInParallel("legacy-channels")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class OperationModeTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string IndexName = "opmode-test";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync("opmode-test");

	[Test]
	public async Task IndexModeOverwritesExistingDocument()
	{
		var slim = new CountdownEvent(1);
		var channel = CreateChannel(OperationMode.Index, slim);
		await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

		channel.TryWrite(new ProductCatalog
		{
			Sku = "OM-001", Name = "Original", Description = "First write",
			Category = "test", Price = 10.0, Tags = ["v1"]
		});
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("First write timed out");

		await Refresh();
		slim.Reset();

		channel.TryWrite(new ProductCatalog
		{
			Sku = "OM-001", Name = "Updated", Description = "Second write (overwrite)",
			Category = "test", Price = 99.0, Tags = ["v2"]
		});
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("Second write timed out");

		await Refresh();

		var search = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{IndexName}/_search");
		search.Body.Should().Contain("\"Updated\"", "index mode should overwrite the document");
		search.Body.Should().Contain("99", "price should be updated to 99");
	}

	[Test]
	public async Task CreateModePreservesExistingDocument()
	{
		var slim = new CountdownEvent(1);
		var channel = CreateChannel(OperationMode.Create, slim);
		await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

		channel.TryWrite(new ProductCatalog
		{
			Sku = "OM-002", Name = "Original", Description = "First write",
			Category = "test", Price = 10.0, Tags = ["v1"]
		});
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("First write timed out");

		await Refresh();
		slim.Reset();

		channel.TryWrite(new ProductCatalog
		{
			Sku = "OM-002", Name = "Duplicate", Description = "Second write (should conflict)",
			Category = "test", Price = 99.0, Tags = ["v2"]
		});
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("Second write timed out");

		await Refresh();

		var search = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{IndexName}/_search");
		search.Body.Should().Contain("\"Original\"", "create mode should preserve the first doc");
		search.Body.Should().NotContain("\"Duplicate\"");
	}

	private IndexChannel<ProductCatalog> CreateChannel(OperationMode mode, CountdownEvent slim) =>
		new(new IndexChannelOptions<ProductCatalog>(Transport)
		{
			IndexFormat = IndexName,
			BulkOperationIdLookup = p => p.Sku,
			OperationMode = mode,
			BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 1 }
		});

	private async Task Refresh()
	{
		var r = await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{IndexName}/_refresh");
		r.ApiCallDetails.HttpStatusCode.Should().Be(200);
	}
}
