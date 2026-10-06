// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
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

/// <summary>
/// Second line of CsCheck tests: model based tests for the parts where bugs hide, the in place compaction of retried requests,
/// buffer growth, response parsing and the retry state machine including HTTP level failures.
/// </summary>
public class BulkSenderModelTests
{
	private static readonly BulkRetryPolicy NoDelay = BulkRetryPolicy.Default with { Backoff = static _ => TimeSpan.Zero };

	private static BulkSender<(BulkAction, Doc), Doc> SenderFor(ScriptedTransport t, BulkRetryPolicy retry = null) =>
		new(new BulkSenderOptions<(BulkAction, Doc), Doc>
		{
			Transport = t.Transport,
			BodyTypeInfo = BulkTestContext.Default.Doc,
			Action = static x => x.Item1,
			Body = static x => x.Item2,
			Retry = retry ?? BulkRetryPolicy.None
		});

	private static int IndexOfId(string id) => int.Parse(id[2..], CultureInfo.InvariantCulture);

	[Test]
	public async Task CompactedRetryRequestsAreByteIdenticalToAFreshSerializationOfTheSubset()
	{
		// documents of very different sizes and every action (including body-less deletes) so offsets and moves are exercised
		var item = Gen.Select(BulkSenderPropertyTests.Actions, Gen.Int[0, 400].Select(n => new string('é', n)), Gen.Int)
			.Select((a, name, n) => (a, new Doc("k", name, n)));
		var gen = item.List[1, 30].SelectMany(items => Gen.Bool.Array[items.Count].Where(m => m.Any(x => x)).Select(mask => (items, mask)));

		await gen.SampleAsync(async x =>
		{
			var (items, mask) = x;
			var first = new ScriptedTransport((attempt, r) => attempt == 0
				? ScriptedResponse.Items(mask.Select(retry => retry ? 503 : 201).ToArray())
				: ScriptedResponse.Items(Enumerable.Repeat(201, ScriptedTransport.CountOperations(r)).ToArray()));
			await SenderFor(first, NoDelay).SendAsync(items.ToArray());

			var subset = items.Where((_, i) => mask[i]).ToArray();
			var fresh = ScriptedTransport.AlwaysSucceeds();
			await SenderFor(fresh).SendAsync(subset);

			first.Requests.Should().HaveCount(2);
			Encoding.UTF8.GetString(first.Requests[1].Body).Should().Be(Encoding.UTF8.GetString(fresh.Requests.Single().Body));
		}, iter: 300);
	}

	[Test]
	public async Task LargeDocumentsGrowTheBufferAndStillMatchTheReference()
	{
		var big = Gen.Int[0, 30_000].Select(n => new string('x', n) + "é😀");
		await Gen.Select(Gen.Int[0, 12], big).SelectMany(x => big.Array[x.Item1]).SampleAsync(async names =>
		{
			var docs = names.Select((n, i) => new Doc($"id{i}", n, i)).ToArray();
			var t = ScriptedTransport.AlwaysSucceeds();
			var sender = BulkSender.Create(t.Transport, BulkTestContext.Default.Doc, static d => BulkAction.Index(d.Id));
			await sender.SendAsync(docs);

			var expected = LegacyBulkWriter.GetBytes(docs, LegacyBulkWriter.ChannelLikeOptions(BulkTestContext.Default), i => new IndexOperation { Id = docs[i].Id });
			t.Requests.Single().Body.Should().Equal(expected);
		}, iter: 60);
	}

	[Test]
	public async Task RealisticBulkResponsesParsePositionally()
	{
		var actionNames = new[] { "index", "create", "update", "delete" };
		var item = Gen.Select(Gen.Int[0, 3], Gen.OneOfConst(200, 201, 204, 400, 404, 409, 429, 500, 503), Gen.Bool, Gen.Int[0, 5], Gen.Int[0, 5]);
		var gen = Gen.Select(item.List[0, 30], Gen.OneOfConst(true, false, (bool?)null));

		await gen.SampleAsync(async x =>
		{
			var (items, errorsFlag) = x;
			var sb = new StringBuilder("{");
			if (errorsFlag is { } flag) sb.Append("\"errors\":").Append(flag ? "true" : "false").Append(',');
			sb.Append("\"took\":5,\"items\":[");
			for (var i = 0; i < items.Count; i++)
			{
				var (a, status, hasError, extraBefore, extraAfter) = items[i];
				if (i > 0) sb.Append(',');
				var error = hasError && status >= 400 ? "\"error\":{\"type\":\"e" + status + "\",\"reason\":\"r\",\"caused_by\":{\"type\":\"c\",\"reason\":\"cr\"}}" : null;
				var parts = new List<string> { "\"_index\":\"idx\"", "\"_id\":\"" + i + "\"", "\"_version\":1", "\"result\":\"created\"",
					"\"_shards\":{\"total\":2,\"successful\":1,\"failed\":0}", "\"_seq_no\":" + i, "\"_primary_term\":1", "\"status\":" + status };
				if (error != null) parts.Add(error);
				// shuffle deterministically from the generated numbers so the property order varies
				parts = parts.OrderBy(p => (p.GetHashCode() * 31 + extraBefore * 7 + extraAfter) & 0xFF).ToList();
				sb.Append("{\"").Append(actionNames[a]).Append("\":{").Append(string.Join(",", parts)).Append("}}");
			}
			sb.Append("]}");

			var t = new ScriptedTransport((_, _) => ScriptedResponse.Raw(sb.ToString()));
			var docs = items.Select((_, i) => (BulkAction.Index($"id{i}"), new Doc($"id{i}", "n", i))).ToArray();
			var response = await SenderFor(t).SendAsync(docs);

			response.Errors.Should().Be(errorsFlag);
			var parsed = response.Items.ToArray();
			parsed.Should().HaveCount(items.Count);
			for (var i = 0; i < items.Count; i++)
			{
				var (a, status, hasError, _, _) = items[i];
				parsed[i].Status.Should().Be(status);
				parsed[i].Action.Should().Be(actionNames[a]);
				if (hasError && status >= 400)
				{
					parsed[i].Error.Should().NotBeNull();
					parsed[i].Error.Type.Should().Be("e" + status);
				}
				else parsed[i].Error.Should().BeNull();
			}
		}, iter: 300);
	}

	[Test]
	public async Task RetryStateMachineMatchesTheModelIncludingHttpLevelOutcomes()
	{
		// per attempt the HTTP outcome: 0 = normal response, 1 = HTTP 429 for the whole request, 2 = HTTP 500
		var itemStatus = Gen.OneOfConst(201, 201, 400, 409, 429, 503);
		var gen = Gen.Select(Gen.Int[1, 20], Gen.Int[0, 4], Gen.Bool)
			.SelectMany(x => Gen.Select(itemStatus.Array[(x.Item2 + 1) * x.Item1], Gen.Frequency((8, Gen.Const(0)), (2, Gen.Const(1)), (1, Gen.Const(2))).Array[x.Item2 + 1],
				(flat, http) => (n: x.Item1, retries: x.Item2, errorsFlag: x.Item3, flat, http)));

		await gen.SampleAsync(async x =>
		{
			var (n, retries, errorsFlag, flat, http) = x;
			int StatusFor(int attempt, int orig) => flat[attempt * n + orig];

			var t = new ScriptedTransport((attempt, r) => http[attempt] switch
			{
				1 => ScriptedResponse.Http(429),
				2 => ScriptedResponse.Http(500),
				_ => errorsFlag
					? ScriptedResponse.Items(ScriptedTransport.IdsOf(r).Select(id => StatusFor(attempt, IndexOfId(id))).ToArray())
					: ScriptedResponse.WithoutErrorsFlag(ScriptedTransport.IdsOf(r).Select(id => StatusFor(attempt, IndexOfId(id))).ToArray())
			});
			var docs = Enumerable.Range(0, n).Select(i => (BulkAction.Index($"id{i}"), new Doc($"id{i}", "n", i))).ToArray();
			var response = await SenderFor(t, NoDelay with { MaxRetries = retries }).SendAsync(docs);

			// reference model
			var final = new int?[n];
			var remaining = Enumerable.Range(0, n).ToList();
			var expectedRequests = new List<int[]>();
			var lastHttpOk = false;
			for (var attempt = 0; attempt <= retries; attempt++)
			{
				expectedRequests.Add(remaining.ToArray());
				if (http[attempt] == 2) { lastHttpOk = false; break; }
				if (http[attempt] == 1) { lastHttpOk = false; continue; }
				lastHttpOk = true;
				foreach (var i in remaining) final[i] = StatusFor(attempt, i);
				remaining = remaining.Where(i => StatusFor(attempt, i) is 429 or 503).ToList();
				if (remaining.Count == 0) break;
			}

			t.Requests.Count.Should().Be(expectedRequests.Count);
			for (var a = 0; a < expectedRequests.Count; a++)
				ScriptedTransport.IdsOf(t.Requests[a]).Should().Equal(expectedRequests[a].Select(i => $"id{i}"));

			if (final.All(f => f is null))
				response.Items.Should().BeNull("no attempt ever returned items");
			else
				response.Items.Select(i => i.Status).Should().Equal(final.Select(f => f!.Value));
			_ = lastHttpOk;
		}, iter: 400);
	}

	[Test]
	public async Task DirectWriteRetriesMatchTheModel()
	{
		var itemStatus = Gen.OneOfConst(201, 201, 400, 409, 429, 502, 503, 504);
		await Gen.Select(Gen.Int[1, 20], Gen.Int[0, 4]).SelectMany(x => itemStatus.Array[(x.Item2 + 1) * x.Item1].Select(flat => (n: x.Item1, retries: x.Item2, flat))).SampleAsync(async x =>
		{
			var (n, retries, flat) = x;
			int StatusFor(int attempt, int orig) => flat[attempt * n + orig];

			var t = new ScriptedTransport((attempt, r) => ScriptedResponse.Items(
				r.Lines.Where((_, i) => i % 2 == 1).Select(l => (int)System.Text.Json.JsonDocument.Parse(l).RootElement.GetProperty("Timestamp").GetDateTimeOffset().ToUnixTimeSeconds())
					.Select(sec => StatusFor(attempt, sec)).ToArray()));
			using var channel = new IndexChannel<TestDocument>(new IndexChannelOptions<TestDocument>(t.Transport) { IndexFormat = "my-index" });
			var docs = Enumerable.Range(0, n).Select(i => new TestDocument { Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(i) }).ToArray();

			var response = await channel.DirectWriteAsync(docs, retries, TimeSpan.Zero);

			var final = new int[n];
			var remaining = Enumerable.Range(0, n).ToList();
			var requests = 0;
			for (var attempt = 0; attempt <= retries && remaining.Count > 0; attempt++)
			{
				requests++;
				foreach (var i in remaining) final[i] = StatusFor(attempt, i);
				remaining = remaining.Where(i => StatusFor(attempt, i) is 429 or 502 or 503 or 504).ToList();
			}

			t.Requests.Count.Should().Be(requests);
			response.Items.Select(i => i.Status).Should().Equal(final);
		}, iter: 250);
	}

	[Test]
	public async Task IngestAllWithRetriesReportsExactlyTheItemsThatNeverRecovered()
	{
		// per item: how many attempts fail transiently (503) before it would succeed, or a permanent 400
		var itemKind = Gen.Frequency((6, Gen.Int[0, 3]), (2, Gen.Const(-1)));
		var gen = Gen.Select(Gen.Int[0, 80], Gen.Int[1, 15], Gen.Int[1, 4]).SelectMany(x => itemKind.Array[x.Item1].Select(kinds => (n: x.Item1, batch: x.Item2, conc: x.Item3, kinds)));

		await gen.SampleAsync(async x =>
		{
			var (n, batch, conc, kinds) = x;
			const int maxRetries = 2;
			var seen = new ConcurrentDictionary<int, int>();
			var t = new ScriptedTransport((_, r) => ScriptedResponse.Items(ScriptedTransport.IdsOf(r).Select(id =>
			{
				var i = IndexOfId(id);
				var attemptForItem = seen.AddOrUpdate(i, 0, (_, c) => c + 1);
				if (kinds[i] == -1) return 400;
				return attemptForItem < kinds[i] ? 503 : 201;
			}).ToArray()));

			var docs = Enumerable.Range(0, n).Select(i => new Doc($"id{i}", "n", i)).ToArray();
			var result = await BulkSender.IngestAllAsync(t.Transport, BulkTestContext.Default.Doc, docs, "p", static d => BulkAction.Index(d.Id),
				new IngestAllOptions { BatchSize = batch, MaxConcurrency = conc, Retry = NoDelay with { MaxRetries = maxRetries } });

			// transient items recover when kinds <= maxRetries (attempts 0..maxRetries); 3 transient failures exhaust 2 retries
			var expectedFailures = Enumerable.Range(0, n).Where(i => kinds[i] == -1 || kinds[i] > maxRetries).ToArray();
			result.Failures.Select(f => f.Position).Should().Equal(expectedFailures.Select(i => (long)i));
			result.Failures.Select(f => f.Item.Status).Should().Equal(expectedFailures.Select(i => kinds[i] == -1 ? 400 : 503));
			for (var i = 0; i < n; i++)
				seen.GetValueOrDefault(i, -1).Should().Be(kinds[i] == -1 ? 0 : Math.Min(kinds[i], maxRetries), $"item {i} is sent once plus once per transient failure up to the retry limit");
		}, iter: 150);
	}
}
