// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsCheck;
using Elastic.Ingest.Elasticsearch.Bulk;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests.Bulk;

/// <summary>Tests the channel export paths and <see cref="IngestChannelBase{TDocument,TChannelOptions}.DirectWriteAsync(System.Collections.Generic.IReadOnlyList{TDocument},int,TimeSpan?,System.Threading.CancellationToken)"/> now that they share the bulk writer.</summary>
public class ChannelBulkPathTests
{
	[Test]
	public async Task StreamingAndInMemoryExportsProduceIdenticalBytes()
	{
		await Gen.Select(BulkSenderPropertyTests.Actions, BulkSenderPropertyTests.Docs).List[0, 20].SampleAsync(async items =>
		{
			var options = new IndexChannelOptions<Doc>(ScriptedTransport.AlwaysSucceeds().Transport) { SerializerContext = BulkTestContext.Default };
			var page = new ArraySegment<Doc>(items.Select(i => i.Item2).ToArray());

			var queue = new System.Collections.Generic.Queue<BulkAction>(items.Select(i => i.Item1));
			var inMemory = BulkRequestDataFactory.GetBytes(page, options, _ => queue.Dequeue());

			queue = new System.Collections.Generic.Queue<BulkAction>(items.Select(i => i.Item1));
			using var stream = new MemoryStream();
			await BulkRequestDataFactory.WriteBufferToStreamAsync(page, stream, options, _ => queue.Dequeue());

			Encoding.UTF8.GetString(stream.ToArray()).Should().Be(Encoding.UTF8.GetString(inMemory.Span));
		}, iter: 200);
	}

	[Test]
	public void LegacyGetBytesNowSupportsDeleteWithoutBodyLine()
	{
		var bytes = BulkRequestDataFactory.GetBytes(
			new[] { new Doc("1", "a", 1), new Doc("2", "b", 2) }.AsSpan(),
			BulkTestContext.Default.Options,
			d => d.N == 1 ? new DeleteOperation { Id = d.Id, Index = "i" } : new IndexOperation { Id = d.Id, Index = "i" },
			d => d);

		Encoding.UTF8.GetString(bytes.Span).Should().Be(
			"{\"delete\":{\"_index\":\"i\",\"_id\":\"1\"}}\n" +
			"{\"index\":{\"_index\":\"i\",\"_id\":\"2\"}}\n{\"Id\":\"2\",\"Name\":\"b\",\"N\":2}\n");
	}

	[Test]
	public void HeaderConversionIsLossless()
	{
		var templates = new System.Collections.Generic.Dictionary<string, string> { ["a"] = "b" };
		BulkAction.From(new IndexOperation { Id = "1", Index = "i", RequireAlias = true, DynamicTemplates = templates })
			.Should().BeEquivalentTo(BulkAction.Index("1", "i").WithRequireAlias().WithDynamicTemplates(templates));
		BulkAction.From(new DeleteOperation { Id = "1" }).Kind.Should().Be(BulkActionKind.Delete);
		BulkAction.From(new UpdateOperation { Id = "1" }).Kind.Should().Be(BulkActionKind.Update);
	}

	private static IndexChannel<TestDocument> Channel(ScriptedTransport t) =>
		new(new IndexChannelOptions<TestDocument>(t.Transport) { IndexFormat = "my-index" });

	private static TestDocument[] Documents(int n) =>
		Enumerable.Range(0, n).Select(i => new TestDocument { Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(i) }).ToArray();

	[Test]
	public async Task DirectWriteRetryKeepsItemsPositionalWithTheInput()
	{
		var t = new ScriptedTransport((attempt, _) => attempt switch
		{
			0 => ScriptedResponse.Items(201, 503, 201, 429),
			1 => ScriptedResponse.Items(201, 503),
			_ => ScriptedResponse.Items(400)
		});
		using var channel = Channel(t);
		var response = await channel.DirectWriteAsync(Documents(4), 3, TimeSpan.Zero);

		t.Requests.Select(r => r.Lines.Length / 2).Should().Equal(4, 2, 1);
		response.Items.Select(i => i.Status).Should().Equal(201, 201, 201, 400);
	}

	[Test]
	public async Task DirectWriteRetriesAnHttp429OfTheWholeRequest()
	{
		var t = new ScriptedTransport((attempt, r) => attempt == 0
			? ScriptedResponse.Http(429)
			: ScriptedResponse.Items(Enumerable.Repeat(201, ScriptedTransport.CountOperations(r)).ToArray()));
		using var channel = Channel(t);
		var response = await channel.DirectWriteAsync(Documents(3), 2, TimeSpan.Zero);

		t.Requests.Should().HaveCount(2);
		response.Items.Should().HaveCount(3);
	}

	[Test]
	public async Task DirectWriteRetryStopsAtTheConfiguredRetries()
	{
		var t = new ScriptedTransport((_, r) => ScriptedResponse.Items(Enumerable.Repeat(503, ScriptedTransport.CountOperations(r)).ToArray()));
		using var channel = Channel(t);
		var response = await channel.DirectWriteAsync(Documents(2), 2, TimeSpan.Zero);

		t.Requests.Should().HaveCount(3);
		response.Items.Select(i => i.Status).Should().Equal(503, 503);
	}
}
