// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CsCheck;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.Indices;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests.Bulk;

public class SubBatchProperties
{
	public sealed class SizedDocument
	{
		public DateTimeOffset Timestamp { get; set; }
		public string Payload { get; set; } = "";
	}

	/// <summary>
	/// A page that is split by <c>OutboundBufferMaxBytes</c>, against a model of the split: the requests hold the page's documents once each
	/// and in order, none is empty, a request is within the budget unless it is one document that is larger by itself, and the packing is
	/// greedy, so a document that opens a request would not have fitted in the one before.
	/// </summary>
	[Test]
	public async Task APageIsSplitInOrderAndPackedWithinTheByteBudget()
	{
		var scenarios = Gen.Select(Gen.Int[0, 300].List[1, 30], Gen.Int[1, 2_000]);
		await scenarios.SampleAsync(async x =>
		{
			var (lengths, maxBytes) = x;
			var n = lengths.Count;
			var t = ScriptedTransport.AlwaysSucceeds();
			using var channel = new IndexChannel<SizedDocument>(new IndexChannelOptions<SizedDocument>(t.Transport)
			{
				IndexFormat = "my-index",
				BufferOptions = new BufferOptions
				{
					InboundBufferMaxSize = n, OutboundBufferMaxSize = n, OutboundBufferMaxBytes = maxBytes, ExportMaxConcurrency = 1,
					ExportMaxRetries = 0, OutboundBufferMaxLifetime = TimeSpan.FromHours(1)
				}
			});

			for (var i = 0; i < n; i++)
				(await channel.WaitToWriteAsync(new SizedDocument { Timestamp = DateTimeOffset.UnixEpoch, Payload = $"{i}:" + new string('x', lengths[i]) })).Should().BeTrue();
			var drained = await Task.Run(() => channel.WaitForDrainAsync(TimeSpan.FromSeconds(20)).AsTask()).ConfigureAwait(false);
			drained.Should().BeTrue();

			var requests = t.Requests;
			// the ids of the documents of each request, and what each document cost on the wire (its action line and its source line)
			var ids = requests.Select(r => r.Lines.Where((_, i) => i % 2 == 1).Select(l => int.Parse(System.Text.Json.JsonDocument.Parse(l).RootElement.GetProperty("Payload").GetString()!.Split(':')[0], System.Globalization.CultureInfo.InvariantCulture)).ToArray()).ToArray();
			ids.SelectMany(i => i).Should().BeEquivalentTo(Enumerable.Range(0, n), o => o.WithStrictOrdering(), "every document once, in order");
			ids.Should().OnlyContain(i => i.Length > 0, "no empty request");

			for (var k = 0; k < requests.Count; k++)
			{
				var length = requests[k].Body.Length;
				(length <= maxBytes || ids[k].Length == 1).Should().BeTrue($"request {k} is {length} bytes, over the budget of {maxBytes}, with {ids[k].Length} documents");
				if (k + 1 < requests.Count)
				{
					var lines = requests[k + 1].Lines;
					var firstDocument = Encoding.UTF8.GetByteCount(lines[0]) + Encoding.UTF8.GetByteCount(lines[1]) + 2;
					(length + firstDocument > maxBytes).Should().BeTrue($"the first document of request {k + 1} would have fitted in request {k}");
				}
			}
		}, iter: 60);
	}

	/// <summary>
	/// The edge of the budget: documents of one size, and a budget that holds exactly k of them, one byte less, or one byte more.
	/// Random sizes almost never land on it, and an off by one there is exactly what this split is for.
	/// </summary>
	[Test]
	public async Task APageIsPackedExactlyAtTheEdgeOfTheBudget()
	{
		var scenarios = Gen.Select(Gen.Int[0, 200], Gen.Int[1, 8], Gen.Int[-1, 1], Gen.Int[1, 25]);
		await scenarios.SampleAsync(async x =>
		{
			var (length, k, delta, n) = x;
			SizedDocument Doc(int i) => new() { Timestamp = DateTimeOffset.UnixEpoch, Payload = $"{i:D3}:" + new string('x', length) };

			async Task<ScriptedTransport> Send(int count, long? maxBytes)
			{
				var t = ScriptedTransport.AlwaysSucceeds();
				using var channel = new IndexChannel<SizedDocument>(new IndexChannelOptions<SizedDocument>(t.Transport)
				{
					IndexFormat = "my-index",
					BufferOptions = new BufferOptions
					{
						InboundBufferMaxSize = count, OutboundBufferMaxSize = count, OutboundBufferMaxBytes = maxBytes, ExportMaxConcurrency = 1,
						ExportMaxRetries = 0, OutboundBufferMaxLifetime = TimeSpan.FromHours(1)
					}
				});
				for (var i = 0; i < count; i++)
					(await channel.WaitToWriteAsync(Doc(i)).ConfigureAwait(false)).Should().BeTrue();
				(await Task.Run(() => channel.WaitForDrainAsync(TimeSpan.FromSeconds(20)).AsTask()).ConfigureAwait(false)).Should().BeTrue();
				return t;
			}

			// what one document costs on the wire
			var eventBytes = (await Send(1, null).ConfigureAwait(false)).Requests.Single().Body.Length;
			var budget = (k * eventBytes) + delta;
			var t = await Send(n, budget).ConfigureAwait(false);

			// k documents fit exactly when the budget is k * eventBytes or more; with one byte less only k - 1 do (at least one per request)
			var perRequest = Math.Max(1, delta < 0 ? k - 1 : k);
			var expected = Enumerable.Range(0, (n + perRequest - 1) / perRequest).Select(r => Math.Min(perRequest, n - (r * perRequest))).ToArray();
			t.Requests.Select(r => r.Lines.Length / 2).Should().BeEquivalentTo(expected, o => o.WithStrictOrdering(),
				$"{n} documents of {eventBytes} bytes with a budget of {budget}");
		}, iter: 80);
	}
}
