// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.Bulk;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Bulk;

public record BulkItDoc(
	[property: JsonPropertyName("id")] string Id,
	[property: JsonPropertyName("name")] string Name,
	[property: JsonPropertyName("n")] int N);

[JsonSerializable(typeof(BulkItDoc))]
internal sealed partial class BulkItContext : JsonSerializerContext;

/*
 * Use case: ingest without (or beside) a mapping context
 *   1. BulkSender: one request per call, mixed insert/update/delete, positional response
 *   2. BulkSender.IngestAllAsync: the one-off "store this list" helper
 *   3. channel.IngestAllAsync on a plain IndexChannelOptions, no Elastic.Mapping involved
 */
[NotInParallel("bulk-sender")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class BulkSenderIntegrationTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string Prefix = "bulk-sender-it";

	private static BulkItDoc[] Docs(int n) => Enumerable.Range(0, n).Select(i => new BulkItDoc($"d{i}", $"name {i}", i)).ToArray();

	private async Task<long> CountAsync(string index)
	{
		await Transport.RequestAsync<StringResponse>(Elastic.Transport.HttpMethod.POST, $"/{index}/_refresh");
		var count = await Transport.RequestAsync<StringResponse>(Elastic.Transport.HttpMethod.GET, $"/{index}/_count");
		count.ApiCallDetails.HttpStatusCode.Should().Be(200);
		return System.Text.Json.JsonDocument.Parse(count.Body).RootElement.GetProperty("count").GetInt64();
	}

	[Test]
	public async Task SenderMixesInsertUpdateAndDeleteAndKeepsItemsPositional()
	{
		var index = $"{Prefix}-mixed";
		await CleanupPrefixAsync(index);

		var sender = new BulkSender<(string Op, BulkItDoc Doc), BulkItDoc>(new BulkSenderOptions<(string Op, BulkItDoc Doc), BulkItDoc>
		{
			Transport = Transport,
			BodyTypeInfo = BulkItContext.Default.BulkItDoc,
			Target = index,
			Body = static x => x.Doc,
			Action = static x => x.Op switch
			{
				"insert" => BulkAction.Index(x.Doc.Id),
				"update" => BulkAction.Update(x.Doc.Id),
				_ => BulkAction.Delete(x.Doc.Id)
			}
		});

		var first = await sender.SendAsync(Docs(3).Select(d => ("insert", d)).ToArray());
		first.AllItemsPersisted().Should().BeTrue();

		var second = await sender.SendAsync(new[]
		{
			("update", new BulkItDoc("d0", "renamed", 0)),
			("delete", new BulkItDoc("d1", "", 0)),
			("insert", new BulkItDoc("d9", "new", 9)),
			("delete", new BulkItDoc("missing", "", 0)),
		});

		second.Items.Select(i => i.Status).Should().Equal(200, 200, 201, 404);
		(await CountAsync(index)).Should().Be(3);

		await CleanupPrefixAsync(index);
	}

	private async Task<long> CountWithoutRefreshAsync(string index)
	{
		// deliberately no _refresh call: visibility must come from the refresh parameter of the _bulk request itself
		var count = await Transport.RequestAsync<StringResponse>(Elastic.Transport.HttpMethod.GET, $"/{index}/_count");
		count.ApiCallDetails.HttpStatusCode.Should().Be(200);
		return System.Text.Json.JsonDocument.Parse(count.Body).RootElement.GetProperty("count").GetInt64();
	}

	[Test]
	[Arguments(BulkRefresh.WaitFor)]
	[Arguments(BulkRefresh.True)]
	public async Task DocumentsAreSearchableWhenTheCallReturnsIfRefreshIsSet(BulkRefresh refresh)
	{
		var index = $"{Prefix}-refresh-{refresh.ToString().ToLowerInvariant()}";
		await CleanupPrefixAsync(index);

		var sender = BulkSender.Create(Transport, BulkItContext.Default.BulkItDoc, static d => BulkAction.Index(d.Id), target: index,
			refresh: refresh, requestTimeout: TimeSpan.FromSeconds(30));
		var response = await sender.SendAsync(Docs(50));

		response.AllItemsPersisted().Should().BeTrue();
		(await CountWithoutRefreshAsync(index)).Should().Be(50);

		await CleanupPrefixAsync(index);
	}

	[Test]
	public async Task ServerGeneratedIdsComeBackAndAddressTheDocuments()
	{
		var index = $"{Prefix}-generated";
		await CleanupPrefixAsync(index);

		var sender = BulkSender.Create(Transport, BulkItContext.Default.BulkItDoc, static _ => BulkAction.Create(), target: index);
		var response = await sender.SendAsync(Docs(5));

		response.AllItemsPersisted().Should().BeTrue();
		var ids = response.Items.Select(i => i.Id).ToArray();
		ids.Should().OnlyContain(id => !string.IsNullOrEmpty(id), "Elasticsearch generated them, the response is the only place to learn them");
		ids.Distinct().Should().HaveCount(5);
		response.Items.Should().OnlyContain(i => i.Index == index);

		// the reported id really addresses the document that was sent at that position
		for (var i = 0; i < 5; i++)
		{
			var get = await Transport.RequestAsync<StringResponse>(Elastic.Transport.HttpMethod.GET, $"/{index}/_doc/{ids[i]}");
			get.ApiCallDetails.HttpStatusCode.Should().Be(200);
			System.Text.Json.JsonDocument.Parse(get.Body).RootElement.GetProperty("_source").GetProperty("name").GetString().Should().Be($"name {i}");
		}

		await CleanupPrefixAsync(index);
	}

	[Test]
	public async Task AnAliasWriteReportsTheConcreteBackingIndex()
	{
		var alias = $"{Prefix}-alias-write";
		var concrete = $"{Prefix}-alias-000001";
		await CleanupPrefixAsync($"{Prefix}-alias");

		var create = await Transport.RequestAsync<StringResponse>(Elastic.Transport.HttpMethod.PUT, $"/{concrete}",
			Elastic.Transport.PostData.String($"{{\"aliases\":{{\"{alias}\":{{\"is_write_index\":true}}}}}}"));
		create.ApiCallDetails.HttpStatusCode.Should().Be(200);

		var sender = BulkSender.Create(Transport, BulkItContext.Default.BulkItDoc, static d => BulkAction.Index(d.Id).WithRequireAlias(), target: alias);
		var response = await sender.SendAsync(Docs(3));

		response.AllItemsPersisted().Should().BeTrue();
		response.Items.Select(i => (i.Id, i.Index)).Should().Equal(("d0", concrete), ("d1", concrete), ("d2", concrete));

		await CleanupPrefixAsync($"{Prefix}-alias");
	}

	[Test]
	public async Task ChannelsReportGeneratedIdsWhenAsked()
	{
		var index = $"{Prefix}-channel-identity";
		await CleanupPrefixAsync(index);

		var options = new IndexChannelOptions<BulkItDocClass>(Transport) { IndexFormat = index, ReturnItemIdentity = true };
		using var channel = new IndexChannel<BulkItDocClass>(options);
		var response = await channel.DirectWriteAsync(new BulkItDocClass { Id = "x", Name = "n1" }, new BulkItDocClass { Id = "y", Name = "n2" });

		response.AllItemsPersisted().Should().BeTrue();
		response.Items.Should().OnlyContain(i => !string.IsNullOrEmpty(i.Id) && i.Index == index);
		var get = await Transport.RequestAsync<StringResponse>(Elastic.Transport.HttpMethod.GET, $"/{index}/_doc/{response.Items.First().Id}");
		get.ApiCallDetails.HttpStatusCode.Should().Be(200);

		await CleanupPrefixAsync(index);
	}

	[Test]
	public async Task StaticHelperStoresAList()
	{
		var index = $"{Prefix}-list";
		await CleanupPrefixAsync(index);

		var result = await BulkSender.IngestAllAsync(Transport, BulkItContext.Default.BulkItDoc, Docs(2_345), index,
			static d => BulkAction.Index(d.Id), new IngestAllOptions { BatchSize = 500, MaxConcurrency = 3 });

		result.Read.Should().Be(2_345);
		result.Batches.Should().Be(5);
		result.Failures.Should().BeEmpty();
		(await CountAsync(index)).Should().Be(2_345);

		await CleanupPrefixAsync(index);
	}

	[Test]
	public async Task StaticHelperReportsRejectedItemsWithTheirPosition()
	{
		var index = $"{Prefix}-reject";
		await CleanupPrefixAsync(index);

		var docs = Docs(5);
		// create twice: the second create of d2 conflicts (409) and is not retryable
		await BulkSender.IngestAllAsync(Transport, BulkItContext.Default.BulkItDoc, new[] { docs[2] }, index, static d => BulkAction.Create(d.Id));
		var result = await BulkSender.IngestAllAsync(Transport, BulkItContext.Default.BulkItDoc, docs, index, static d => BulkAction.Create(d.Id));

		result.Failures.Select(f => (f.Position, f.Item.Status)).Should().Equal((2L, 409));
		(await CountAsync(index)).Should().Be(5);

		await CleanupPrefixAsync(index);
	}

	[Test]
	public async Task ChannelIngestAllWorksWithoutAMappingContext()
	{
		var index = $"{Prefix}-channel";
		await CleanupPrefixAsync(index);

		var options = new IndexChannelOptions<BulkItDocClass>(Transport)
		{
			IndexFormat = index,
			BulkOperationIdLookup = static d => d.Id,
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 100, ExportMaxConcurrency = 2 }
		};
		using var channel = new IndexChannel<BulkItDocClass>(options);

		var docs = Enumerable.Range(0, 1_050).Select(i => new BulkItDocClass { Id = $"d{i}", Name = $"n{i}" }).ToArray();
		var result = await channel.IngestAllAsync(docs);

		result.Read.Should().Be(1_050);
		result.RetriesExhausted.Should().Be(0);
		(await CountAsync(index)).Should().Be(1_050);

		await CleanupPrefixAsync(index);
	}
}

public class BulkItDocClass
{
	public string Id { get; set; } = null!;
	public string Name { get; set; } = null!;
}
