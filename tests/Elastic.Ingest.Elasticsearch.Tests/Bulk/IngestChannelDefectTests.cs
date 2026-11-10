// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.Indices;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests.Bulk;

/// <summary>Defects of the Elasticsearch channel found by reading it, each pinned by a spec.</summary>
public class IngestChannelDefectTests
{
	private static IndexChannel<TestDocument> Channel(ScriptedTransport t, ConcurrentBag<int> exhausted, long? maxBytes = null, int batch = 10) =>
		new(new IndexChannelOptions<TestDocument>(t.Transport)
		{
			IndexFormat = "my-index",
			ExportMaxRetriesCallback = items => { foreach (var d in items) exhausted.Add((int)(d.Timestamp - DateTimeOffset.UnixEpoch).TotalSeconds); },
			BufferOptions = new BufferOptions
			{
				OutboundBufferMaxSize = batch,
				OutboundBufferMaxBytes = maxBytes,
				ExportMaxConcurrency = 1,
				ExportMaxRetries = 2,
				ExportBackoffPeriod = _ => TimeSpan.FromMilliseconds(1),
				OutboundBufferMaxLifetime = TimeSpan.FromHours(1)
			}
		});

	private static int[] Seconds(CapturedRequest r) =>
		r.Lines.Where((_, i) => i % 2 == 1)
			.Select(l => (int)(JsonDocument.Parse(l).RootElement.GetProperty("Timestamp").GetDateTimeOffset() - DateTimeOffset.UnixEpoch).TotalSeconds)
			.ToArray();

	private static async Task<bool> WriteAndDrain(IndexChannel<TestDocument> channel, int count)
	{
		for (var i = 0; i < count; i++)
			(await channel.WaitToWriteAsync(new TestDocument { Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(i) })).Should().BeTrue();
		return await Task.Run(() => channel.WaitForDrainAsync(TimeSpan.FromSeconds(10)).AsTask());
	}

	// 7. a request the server answered with 429 is sent again
	[Test]
	public async Task AWholeRequestThatIsAnsweredWith429IsRetried()
	{
		var exhausted = new ConcurrentBag<int>();
		var t = new ScriptedTransport((attempt, r) => attempt == 0
			? ScriptedResponse.Http(429)
			: ScriptedResponse.Items(Enumerable.Repeat(201, ScriptedTransport.CountOperations(r)).ToArray()));
		using var channel = Channel(t, exhausted);

		var drained = await WriteAndDrain(channel, 3);

		drained.Should().BeTrue();
		t.Requests.Should().HaveCount(2, "the first request was rejected as a whole and is sent again");
		Seconds(t.Requests[1]).Should().BeEquivalentTo([0, 1, 2]);
		exhausted.Should().BeEmpty();
	}

	// 11. a sub-batch that fails as a whole is the only one sent again
	[Test]
	public async Task OnlyTheSubBatchThatFailedIsRetried()
	{
		var exhausted = new ConcurrentBag<int>();
		// a budget of one byte makes every document its own sub-batch: three requests for the page
		var t = new ScriptedTransport((attempt, r) => attempt == 1
			? ScriptedResponse.Http(429)
			: ScriptedResponse.Items(Enumerable.Repeat(201, ScriptedTransport.CountOperations(r)).ToArray()));
		using var channel = Channel(t, exhausted, maxBytes: 1);

		var drained = await WriteAndDrain(channel, 3);

		drained.Should().BeTrue();
		t.Requests.Should().HaveCount(4, "three sub-batches, then the one that was rejected");
		Seconds(t.Requests[3]).Should().Equal(1);
		exhausted.Should().BeEmpty();
	}

	// 11c. a sub-batch that got no answer is reported for its documents only, and is not sent again
	[Test]
	public async Task ASubBatchWithoutAnAnswerIsReportedAsRejectedForItsDocumentsOnly()
	{
		var exhausted = new ConcurrentBag<int>();
		var rejected = new ConcurrentBag<int>();
		var t = new ScriptedTransport((attempt, r) => attempt == 1
			? ScriptedResponse.Http(0)
			: ScriptedResponse.Items(Enumerable.Repeat(201, ScriptedTransport.CountOperations(r)).ToArray()));
		var options = new IndexChannelOptions<TestDocument>(t.Transport)
		{
			IndexFormat = "my-index",
			ServerRejectionCallback = items => { foreach (var (d, _) in items) rejected.Add((int)(d.Timestamp - DateTimeOffset.UnixEpoch).TotalSeconds); },
			ExportMaxRetriesCallback = items => { foreach (var d in items) exhausted.Add((int)(d.Timestamp - DateTimeOffset.UnixEpoch).TotalSeconds); },
			BufferOptions = new BufferOptions
			{
				OutboundBufferMaxSize = 10, OutboundBufferMaxBytes = 1, ExportMaxConcurrency = 1, ExportMaxRetries = 2,
				ExportBackoffPeriod = _ => TimeSpan.FromMilliseconds(1), OutboundBufferMaxLifetime = TimeSpan.FromHours(1)
			}
		};
		using var channel = new IndexChannel<TestDocument>(options);

		var drained = await WriteAndDrain(channel, 3);

		drained.Should().BeTrue();
		t.Requests.Should().HaveCount(3, "it may have reached the server, so the documents are not sent again");
		rejected.Should().BeEquivalentTo([1], "the caller decides what to do with the documents that got no answer");
		exhausted.Should().BeEmpty();
	}

	// 11b. a sub-batch that failed does not shift the results of the others onto the wrong documents
	[Test]
	public async Task AFailedSubBatchDoesNotShiftTheResultsOfTheOthers()
	{
		var exhausted = new ConcurrentBag<int>();
		var rejected = new ConcurrentBag<int>();
		// sub-batch 0 fails as a whole with 500; sub-batch 1 answers 400 for its document; sub-batch 2 succeeds
		var t = new ScriptedTransport((attempt, r) => attempt switch
		{
			0 => ScriptedResponse.Http(500),
			1 => ScriptedResponse.Items(400),
			_ => ScriptedResponse.Items(Enumerable.Repeat(201, ScriptedTransport.CountOperations(r)).ToArray())
		});
		var options = new IndexChannelOptions<TestDocument>(t.Transport)
		{
			IndexFormat = "my-index",
			ServerRejectionCallback = items => { foreach (var (d, _) in items) rejected.Add((int)(d.Timestamp - DateTimeOffset.UnixEpoch).TotalSeconds); },
			ExportMaxRetriesCallback = items => { foreach (var d in items) exhausted.Add((int)(d.Timestamp - DateTimeOffset.UnixEpoch).TotalSeconds); },
			BufferOptions = new BufferOptions
			{
				OutboundBufferMaxSize = 10, OutboundBufferMaxBytes = 1, ExportMaxConcurrency = 1, ExportMaxRetries = 0,
				ExportBackoffPeriod = _ => TimeSpan.FromMilliseconds(1), OutboundBufferMaxLifetime = TimeSpan.FromHours(1)
			}
		};
		using var channel = new IndexChannel<TestDocument>(options);

		_ = await WriteAndDrain(channel, 3);

		// 0: its sub-batch failed with a 500, which is not retryable. 1: the 400 is its own. 2 went through.
		rejected.Should().BeEquivalentTo([0, 1], "each rejection belongs to the document of its own sub-batch");
	}
}
