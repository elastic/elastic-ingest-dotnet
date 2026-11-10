// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CsCheck;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests.Bulk;

/// <summary>The two ends of a bulk request: the NDJSON the channel writes, and the response it reads back.</summary>
public class BulkWireProperties
{
	/// <summary>
	/// The body is NDJSON whatever the documents hold: two lines per document, an action and a source, each valid JSON on its own line
	/// (quotes, backslashes, newlines and non ASCII characters in a document are escaped, never raw), a final newline, and the
	/// source round-trips to the document that was written.
	/// </summary>
	[Test]
	public async Task TheRequestBodyIsNdjsonWhateverTheDocumentsHold()
	{
		var text = Gen.Char[' ', '~'].Select(c => c).List[0, 40].Select(cs => new string(cs.ToArray()));
		var tricky = Gen.OneOfConst("\n", "\r\n", "\"", "\\", "\u2028", "é", "日本語", "\t", "{\"a\":1}", "\0").List[0, 5].Select(p => string.Concat(p));
		var payloads = Gen.Select(text, tricky).Select((a, b) => a + b).List[1, 25];
		await payloads.SampleAsync(async list =>
		{
			var t = ScriptedTransport.AlwaysSucceeds();
			using var channel = new IndexChannel<BulkWire.Doc>(new IndexChannelOptions<BulkWire.Doc>(t.Transport)
			{
				IndexFormat = "my-index",
				BufferOptions = new BufferOptions
				{
					InboundBufferMaxSize = list.Count, OutboundBufferMaxSize = list.Count, ExportMaxConcurrency = 1,
					ExportMaxRetries = 0, OutboundBufferMaxLifetime = TimeSpan.FromHours(1)
				}
			});

			for (var i = 0; i < list.Count; i++)
				(await channel.WaitToWriteAsync(new BulkWire.Doc { Timestamp = DateTimeOffset.UnixEpoch, Payload = list[i] }).ConfigureAwait(false)).Should().BeTrue();
			(await Task.Run(() => channel.WaitForDrainAsync(TimeSpan.FromSeconds(20)).AsTask()).ConfigureAwait(false)).Should().BeTrue();

			var request = t.Requests.Should().ContainSingle().Subject;
			var body = Encoding.UTF8.GetString(request.Body);
			body.Should().EndWith("\n");
			var lines = body.Split('\n', StringSplitOptions.None)[..^1];
			lines.Length.Should().Be(list.Count * 2, "an action line and a source line for every document");
			for (var i = 0; i < list.Count; i++)
			{
				using var action = JsonDocument.Parse(lines[i * 2]);
				action.RootElement.EnumerateObject().Should().ContainSingle().Which.Name.Should().BeOneOf("index", "create");
				using var source = JsonDocument.Parse(lines[(i * 2) + 1]);
				source.RootElement.GetProperty("Payload").GetString().Should().Be(list[i], "the source is the document, escaped and read back");
			}
		}, iter: 80);
	}

	/// <summary>
	/// A bulk response is read item by item: as many items as the server sent, in its order, with the status of each, an error where the
	/// server gave one, and the <c>errors</c> flag as the server reported it.
	/// </summary>
	[Test]
	public void AResponseIsReadItemByItemInTheServersOrder()
	{
		var statuses = new[] { 200, 201, 400, 404, 409, 429, 500, 503 };
		var scenarios = Gen.Select(Gen.OneOfConst(statuses).List[0, 60], Gen.OneOfConst("index", "create", "update", "delete"), Gen.Bool);
		scenarios.Sample(x =>
		{
			var (items, action, includeErrorsFlag) = x;
			var hasErrors = items.Any(s => s >= 300);
			var json = new StringBuilder("{");
			if (includeErrorsFlag) json.Append(CultureInfo.InvariantCulture, $"\"errors\":{(hasErrors ? "true" : "false")},");
			json.Append("\"items\":[");
			json.Append(string.Join(",", items.Select(s => s >= 400
				? $"{{\"{action}\":{{\"status\":{s},\"error\":{{\"type\":\"t{s}\",\"reason\":\"r{s}\"}}}}}}"
				: $"{{\"{action}\":{{\"status\":{s}}}}}")));
			json.Append("]}");

			var transport = new DistributedTransport(new TransportConfiguration(new SingleNodePool(new Uri("http://localhost:9200")),
				new InMemoryRequestInvoker(Encoding.UTF8.GetBytes(json.ToString()))));
			var response = transport.Request<BulkResponse>(Elastic.Transport.HttpMethod.POST, "_bulk", PostData.String(""));

			response.Items.Select(i => i.Status).Should().Equal(items, "the statuses, in the server's order");
			response.Items.Where(i => i.Status >= 400).All(i => i.Error != null && i.Error.Type == "t" + i.Status).Should().BeTrue("an item that failed carries the error the server gave");
			response.Items.Where(i => i.Status < 400).All(i => i.Error == null).Should().BeTrue("an item that succeeded carries none");
			response.Errors.Should().Be(includeErrorsFlag ? hasErrors : null, "the flag as the server said it, unknown when it did not");
		}, iter: 150);
	}
}

internal static class BulkWire
{
	public sealed class Doc
	{
		public DateTimeOffset Timestamp { get; set; }
		public string Payload { get; set; } = "";
	}
}
