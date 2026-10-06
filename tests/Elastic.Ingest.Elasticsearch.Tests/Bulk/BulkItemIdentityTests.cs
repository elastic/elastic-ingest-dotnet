// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CsCheck;
using Elastic.Ingest.Elasticsearch.Bulk;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests.Bulk;

/// <summary>
/// <c>_id</c> and <c>_index</c> of every response item (#216): server generated ids and alias resolved indices must come back on every path.
/// The scripted transport answers like the server does, see <see cref="ScriptedTransport"/>.
/// </summary>
public class BulkItemIdentityTests
{
	private const string IdentityFilter = "items.*._id,items.*._index";
	private static readonly BulkRetryPolicy NoDelay = BulkRetryPolicy.Default with { Backoff = static _ => TimeSpan.Zero };

	private static BulkSender<(BulkAction, Doc), Doc> SenderFor(ScriptedTransport t, BulkItemIdentity identity = BulkItemIdentity.Auto, string target = null, BulkRetryPolicy retry = null) =>
		new(new BulkSenderOptions<(BulkAction, Doc), Doc>
		{
			Transport = t.Transport,
			BodyTypeInfo = BulkTestContext.Default.Doc,
			Action = static x => x.Item1,
			Body = static x => x.Item2,
			Target = target,
			ItemIdentity = identity,
			Retry = retry ?? BulkRetryPolicy.None
		});

	private static (BulkAction, Doc)[] With(int n, Func<int, BulkAction> action) =>
		Enumerable.Range(0, n).Select(i => (action(i), new Doc($"id{i}", "n", i))).ToArray();

	private static bool Asked(CapturedRequest r) => r.PathAndQuery.Contains(IdentityFilter, StringComparison.Ordinal);

	// ---- when the response is asked for identity ----

	[Test]
	public async Task RequestsWhereEveryActionHasAnIdAndAConcreteIndexDoNotPayForIdentity()
	{
		var t = new ScriptedTransport((_, r) => ScriptedResponse.Items(Enumerable.Repeat(201, ScriptedTransport.CountOperations(r)).ToArray()));
		var response = await SenderFor(t).SendAsync(With(4, i => BulkAction.Index($"id{i}", "idx")));

		Asked(t.Requests.Single()).Should().BeFalse();
		response.Items.Should().OnlyContain(i => i.Id == null && i.Index == null);
		var items = response.Items.ToArray();
		items[0].Should().BeSameAs(items[3], "items without per item state are still shared, so the common path allocates nothing extra");
	}

	[Test]
	public async Task GeneratedIdsAreReported()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var response = await SenderFor(t, target: "products").SendAsync(With(3, _ => BulkAction.Index()));

		Asked(t.Requests.Single()).Should().BeTrue();
		response.Items.Select(i => i.Id).Should().Equal("gen-0-0", "gen-0-1", "gen-0-2");
		response.Items.Should().OnlyContain(i => i.Index == "products");
	}

	[Test]
	public async Task AnAliasWriteReportsTheConcreteIndexBehindIt()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var response = await SenderFor(t).SendAsync(With(2, i => BulkAction.Index($"id{i}", "orders-write").WithRequireAlias()));

		Asked(t.Requests.Single()).Should().BeTrue();
		response.Items.Select(i => (i.Id, i.Index)).Should().Equal(("id0", "orders-write-000001"), ("id1", "orders-write-000001"));
	}

	[Test]
	public async Task OneIdLessItemMakesTheWholeRequestReportIdentityForEveryItem()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var response = await SenderFor(t, target: "p").SendAsync(With(3, i => i == 1 ? BulkAction.Create() : BulkAction.Index($"id{i}")));

		response.Items.Select(i => i.Id).Should().Equal("id0", "gen-0-1", "id2");
	}

	[Test]
	public async Task AlwaysReportsEvenWhenEveryIdIsKnownAndNeverSuppressesEvenForGeneratedIds()
	{
		var always = ScriptedTransport.AlwaysSucceeds();
		var withIds = await SenderFor(always, BulkItemIdentity.Always, "p").SendAsync(With(2, i => BulkAction.Index($"id{i}")));
		withIds.Items.Select(i => (i.Id, i.Index)).Should().Equal(("id0", "p"), ("id1", "p"));

		var never = ScriptedTransport.AlwaysSucceeds();
		var generated = await SenderFor(never, BulkItemIdentity.Never, "p").SendAsync(With(2, _ => BulkAction.Index()));
		Asked(never.Requests.Single()).Should().BeFalse();
		generated.Items.Should().OnlyContain(i => i.Id == null && i.Index == null);
	}

	[Test]
	public async Task UpdatesAndDeletesWithExplicitIdsDoNotAskForIdentity()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		await SenderFor(t).SendAsync(With(4, i => i % 2 == 0 ? BulkAction.Update($"id{i}", "idx") : BulkAction.Delete($"id{i}", "idx")));
		Asked(t.Requests.Single()).Should().BeFalse();
	}

	[Test]
	public void AnUnknownIdentityModeIsRejectedWhenTheSenderIsCreated()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		Action act = () => SenderFor(t, (BulkItemIdentity)42);
		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Test]
	public async Task TheCreateHelperPassesTheModeThrough()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var sender = BulkSender.Create(t.Transport, BulkTestContext.Default.Doc, static d => BulkAction.Index(d.Id), target: "p", itemIdentity: BulkItemIdentity.Always);
		var response = await sender.SendAsync(new[] { new Doc("a", "n", 1) });

		Asked(t.Requests.Single()).Should().BeTrue();
		response.Items.Single().Id.Should().Be("a");
	}

	[Test]
	public async Task IdentityComposesWithRefreshAndTarget()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var sender = new BulkSender<Doc, Doc>(new BulkSenderOptions<Doc, Doc>
		{
			Transport = t.Transport,
			BodyTypeInfo = BulkTestContext.Default.Doc,
			Action = static _ => BulkAction.Index(),
			Body = static d => d,
			Target = "p",
			Refresh = BulkRefresh.WaitFor
		});
		await sender.SendAsync(new[] { new Doc("a", "n", 1) });

		t.Requests.Single().PathAndQuery.Should().Be("p/_bulk?refresh=wait_for&filter_path=errors,error,items.*.status,items.*.error,items.*.result,items.*._version,items.*._id,items.*._index");
	}

	// ---- every path a response takes ----

	[Test]
	public async Task RetriesKeepAskingAndEachPositionKeepsTheIdentityOfTheAttemptThatSettledIt()
	{
		var t = new ScriptedTransport((attempt, r) => attempt == 0
			? ScriptedResponse.Items(201, 503, 201, 503)
			: ScriptedResponse.Items(Enumerable.Repeat(201, ScriptedTransport.CountOperations(r)).ToArray()));
		var response = await SenderFor(t, target: "p", retry: NoDelay).SendAsync(With(4, _ => BulkAction.Index()));

		t.Requests.Should().HaveCount(2);
		t.Requests.Should().OnlyContain(r => Asked(r), "the retried subset is asked for identity like the request it came from");
		// position 1 and 3 were re-sent as positions 0 and 1 of the second request
		response.Items.Select(i => i.Id).Should().Equal("gen-0-0", "gen-1-0", "gen-0-2", "gen-1-1");
		response.Items.Should().OnlyContain(i => i.Index == "p");
	}

	[Test]
	public async Task IngestAllFailuresCarryTheIdAndIndexOfTheFailedItem()
	{
		var t = new ScriptedTransport((_, r) => ScriptedResponse.Items(Enumerable.Range(0, ScriptedTransport.CountOperations(r)).Select(i => i == 1 ? 400 : 201).ToArray()));
		var sender = SenderFor(t, target: "p").AsDocSender();
		var result = await sender.IngestAllAsync(With(5, _ => BulkAction.Index()), new IngestAllOptions { BatchSize = 3, MaxConcurrency = 1 });

		// positions 1 (batch 1) and 4 (batch 2, position 1 of that request) fail
		result.Failures.Select(f => (f.Position, f.Item.Id, f.Item.Index, f.Item.Status)).Should().Equal(
			(1L, "gen-0-1", "p", 400),
			(4L, "gen-1-1", "p", 400));
	}

	[Test]
	public async Task ARequestThatFailedAsAWholeHasNoItemsToReportIdentityFor()
	{
		var t = new ScriptedTransport((_, _) => ScriptedResponse.Http(500));
		var result = await SenderFor(t, target: "p").AsDocSender().IngestAllAsync(With(2, _ => BulkAction.Index()));

		result.Failures.Should().HaveCount(2);
		result.Failures.Should().OnlyContain(f => f.Item.Id == null && f.Item.Index == null);
	}

	// ---- channels ----

	private static IndexChannel<TestDocument> Channel(ScriptedTransport t, bool identity, bool readOnlyMemory, long? maxBytes = null)
	{
		var options = new IndexChannelOptions<TestDocument>(t.Transport)
		{
			IndexFormat = "my-index",
			ReturnItemIdentity = identity,
			BufferOptions = new Elastic.Channels.BufferOptions { OutboundBufferMaxBytes = maxBytes, OutboundBufferMaxSize = 100 }
		};
#pragma warning disable CS0618
		options.UseReadOnlyMemory = readOnlyMemory;
#pragma warning restore CS0618
		return new IndexChannel<TestDocument>(options);
	}

	private static TestDocument[] Documents(int n) => Enumerable.Range(0, n).Select(i => new TestDocument { Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(i) }).ToArray();

	[Test]
	public async Task ChannelsDoNotAskForIdentityByDefault()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		using var channel = Channel(t, identity: false, readOnlyMemory: true);
		var response = await channel.DirectWriteAsync(Documents(2));

		t.Requests.Single().PathAndQuery.Should().Be("my-index/_bulk?filter_path=errors,error,items.*.status,items.*.error,items.*.result,items.*._version");
		response.Items.Should().OnlyContain(i => i.Id == null && i.Index == null);
	}

	[Test]
	[Arguments(true, null)]
	[Arguments(false, null)]
	[Arguments(true, 200L)]
	[Arguments(false, 200L)]
	public async Task ChannelsReportIdentityOnEveryExportPathWhenAsked(bool readOnlyMemory, long? maxBytes)
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		using var channel = Channel(t, identity: true, readOnlyMemory, maxBytes);
		var response = await channel.DirectWriteAsync(Documents(6));

		if (maxBytes is not null) t.Requests.Count.Should().BeGreaterThan(1, "the byte budget splits the batch into sub-requests whose items are merged");
		t.Requests.Should().OnlyContain(r => Asked(r));
		var items = response.Items.ToArray();
		items.Should().HaveCount(6);
		items.Should().OnlyContain(i => i.Id != null && i.Index == "my-index");
		items.Select(i => i.Id).Distinct().Should().HaveCount(6, "every position keeps the id of its own response item");
	}

	[Test]
	public async Task PushedEventsReachTheResponseCallbackWithTheirIdentity()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		using var done = new System.Threading.CountdownEvent(1);
		BulkResponse seen = null;
		var options = new IndexChannelOptions<TestDocument>(t.Transport)
		{
			IndexFormat = "my-index",
			ReturnItemIdentity = true,
			BufferOptions = new Elastic.Channels.BufferOptions { OutboundBufferMaxSize = 3, WaitHandle = done },
			ExportResponseCallback = (r, _) => seen = r
		};
		using var channel = new IndexChannel<TestDocument>(options);
		foreach (var d in Documents(3)) channel.TryWrite(d);
		done.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue();

		seen.Items.Should().HaveCount(3).And.OnlyContain(i => i.Id != null && i.Index == "my-index");
	}

	[Test]
	public async Task PullingASequenceThroughAChannelReportsIdentityToTheCallback()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var ids = new System.Collections.Concurrent.ConcurrentBag<string>();
		var options = new IndexChannelOptions<TestDocument>(t.Transport)
		{
			IndexFormat = "my-index",
			ReturnItemIdentity = true,
			BufferOptions = new Elastic.Channels.BufferOptions { OutboundBufferMaxSize = 4, ExportMaxConcurrency = 1 },
			ExportResponseCallback = (r, _) => { foreach (var i in r.Items) ids.Add(i.Id); }
		};
		using var channel = new IndexChannel<TestDocument>(options);
		await channel.IngestAllAsync(Documents(10));

		ids.Should().HaveCount(10).And.OnlyContain(id => id != null);
	}

	// ---- parsing ----

	private static BulkResponse Parse(string body) =>
		JsonSerializer.Deserialize(body, IngestSerializationContext.Default.BulkResponse)!;

	[Test]
	public void IdAndIndexAreReadAndNestedObjectsOfTheSameNameAreIgnored()
	{
		var response = Parse("""
			{"errors":false,"items":[
			  {"index":{"_index":"idx-1","_id":"a","_version":1,"_shards":{"status":500,"_id":"nested","_index":"nested","total":2},"tags":[{"status":404,"_id":"x"}],"status":201}},
			  {"create":{"_id":null,"_index":"idx-1","status":201}},
			  {"index":{"_index":12,"_id":"c","status":200}}
			]}
			""");

		var items = response.Items.ToArray();
		items.Select(i => (i.Action, i.Status, i.Id, i.Index)).Should().Equal(
			("index", 201, "a", "idx-1"),
			("create", 201, null, "idx-1"),
			("index", 200, "c", null));
	}

	[Test]
	public void IdAndIndexOfTheWrongTypeAreSkippedWholeAndNeverMistakenForTheItem()
	{
		var response = Parse("""
			{"items":[
			  {"index":{"_id":{"status":500,"error":{"type":"x"}},"_index":["status",{"status":404}],"status":201}},
			  {"index":{"_index":{"_id":"nested"},"_id":["a"],"status":200}}
			]}
			""");

		var items = response.Items.ToArray();
		items.Select(i => (i.Status, i.Id, i.Index, HasError: i.Error != null)).Should().Equal((201, null, null, false), (200, null, null, false));
	}

	[Test]
	public void ConsecutiveItemsOfTheSameIndexShareOneStringInstance()
	{
		var response = Parse("""{"items":[{"index":{"_index":"i","_id":"1","status":201}},{"index":{"_index":"i","_id":"2","status":201}},{"index":{"_index":"j","_id":"3","status":201}},{"index":{"_index":"i","_id":"4","status":201}}]}""");
		var items = response.Items.ToArray();

		items[1].Index.Should().BeSameAs(items[0].Index);
		items[2].Index.Should().Be("j");
		items[3].Index.Should().Be("i");
	}

	[Test]
	public void ItemsThatCarryIdentityAreNeverTheSharedInstances()
	{
		var response = Parse("""{"items":[{"index":{"status":201}},{"index":{"status":201}},{"index":{"_id":"1","status":201}},{"index":{"_index":"i","status":201}}]}""");
		var items = response.Items.ToArray();

		items[0].Should().BeSameAs(items[1]);
		items[2].Should().NotBeSameAs(items[0]);
		items[3].Should().NotBeSameAs(items[0]);
		items[0].Id.Should().BeNull();
		items[0].Index.Should().BeNull();
	}

	[Test]
	public void IdentityFieldsSurviveAWriteAndReadRoundTrip()
	{
		var item = new BulkResponseItem { Action = "index", Status = 201, Id = "gen-é\"\n", Index = "idx-000001" };
		var json = JsonSerializer.Serialize(item, IngestSerializationContext.Default.BulkResponseItem);
		var back = JsonSerializer.Deserialize(json, IngestSerializationContext.Default.BulkResponseItem)!;

		(back.Action, back.Status, back.Id, back.Index).Should().Be(("index", 201, "gen-é\"\n", "idx-000001"));
	}

	// ---- properties ----

	private static readonly string[] Actions = ["index", "create", "update", "delete"];
	private static readonly Gen<string> Text = Gen.OneOfConst("a", "id-1", "é", "日本語", "😀", "with \"quote\"", "back\\slash", "line\nbreak", "", "gen-0-0");

	[Test]
	public async Task ResponsesParseToExactlyTheirIdentityInAnyPropertyOrder()
	{
		var indexNames = Gen.OneOfConst("idx-a", "idx-b", ".ds-logs-2026.10.06-000001", "é");
		var item = Gen.Select(Gen.Int[0, 3], Gen.OneOfConst(200, 201, 204, 400, 404, 409, 429, 503), Gen.Bool, Gen.Int[0, 2], Text, Gen.Int[0, 2], indexNames, Gen.Int);
		await item.List[0, 40].SampleAsync(items =>
		{
			var ms = new System.IO.MemoryStream();
			using (var w = new Utf8JsonWriter(ms))
			{
				w.WriteStartObject();
				w.WriteStartArray("items");
				foreach (var (a, status, hasError, idMode, id, indexMode, index, order) in items)
				{
					w.WriteStartObject();
					w.WriteStartObject(Actions[a]);
					var props = new List<(int Key, Action Write)>
					{
						(order & 7, () => w.WriteNumber("status", status)),
						((order >> 3) & 7, () => w.WriteNumber("_version", 1)),
						((order >> 6) & 7, () => { w.WriteStartObject("_shards"); w.WriteNumber("status", 500); w.WriteString("_id", "nested"); w.WriteString("_index", "nested"); w.WriteEndObject(); }),
					};
					if (hasError && status >= 400) props.Add(((order >> 9) & 7, () => { w.WriteStartObject("error"); w.WriteString("type", "e" + status); w.WriteString("reason", "r"); w.WriteEndObject(); }));
					if (idMode == 1) props.Add(((order >> 12) & 7, () => w.WriteString("_id", id)));
					if (idMode == 2) props.Add(((order >> 12) & 7, () => w.WriteNull("_id")));
					if (indexMode == 1) props.Add(((order >> 15) & 7, () => w.WriteString("_index", index)));
					foreach (var p in props.OrderBy(p => p.Key)) p.Write();
					w.WriteEndObject();
					w.WriteEndObject();
				}
				w.WriteEndArray();
				w.WriteEndObject();
			}

			var response = JsonSerializer.Deserialize(ms.ToArray(), IngestSerializationContext.Default.BulkResponse)!;
			var parsed = response.Items.ToArray();
			parsed.Should().HaveCount(items.Count);

			for (var i = 0; i < items.Count; i++)
			{
				var (a, status, hasError, idMode, id, indexMode, index, _) = items[i];
				parsed[i].Action.Should().Be(Actions[a]);
				parsed[i].Status.Should().Be(status);
				parsed[i].Id.Should().Be(idMode == 1 ? id : null);
				parsed[i].Index.Should().Be(indexMode == 1 ? index : null);
				(parsed[i].Error != null).Should().Be(hasError && status >= 400);

				var plain = parsed[i].Error is null && parsed[i].Id is null && parsed[i].Index is null && status is 200 or 201;
				if (plain) continue;
				// anything carrying state must be its own instance, shared instances are immutable and stateless
				parsed.Where((p, j) => j != i).Should().NotContain(p => ReferenceEquals(p, parsed[i]));
			}

			for (var i = 1; i < items.Count; i++)
				if (parsed[i].Index is not null && parsed[i - 1].Index is not null && parsed[i].Index == parsed[i - 1].Index)
					parsed[i].Index.Should().BeSameAs(parsed[i - 1].Index);
			return Task.CompletedTask;
		}, iter: 300);
	}

	private static readonly Gen<BulkAction> AnyAction = Gen.Select(Gen.Int[0, 4], Gen.OneOfConst<string>(null, "", "  ", "id1"), Gen.Bool, Gen.OneOfConst<string>(null, "idx", "alias"), (kind, id, alias, index) =>
	{
		var explicitId = string.IsNullOrWhiteSpace(id) ? "needed" : id;
		BulkAction a = kind switch
		{
			0 => BulkAction.Index(id, index),
			1 => BulkAction.Create(id, index),
			2 => BulkAction.Update(explicitId, index),
			3 => BulkAction.Delete(explicitId, index),
			_ => BulkAction.ScriptedHashUpsert(explicitId, new HashedBulkUpdate("f", "h"), index)
		};
		return alias ? a.WithRequireAlias() : a;
	});

	private static bool NeedsIdentity(BulkAction a) =>
		a.RequireAlias || (a.Kind is BulkActionKind.Index or BulkActionKind.Create && string.IsNullOrWhiteSpace(a.Id));

	[Test]
	public async Task IdentityIsRequestedExactlyWhenARequestHasSomethingOnlyTheServerKnows()
	{
		await Gen.Select(AnyAction.List[1, 12], Gen.OneOfConst(BulkItemIdentity.Auto, BulkItemIdentity.Always, BulkItemIdentity.Never)).SampleAsync(async x =>
		{
			var (actions, mode) = x;
			var t = ScriptedTransport.AlwaysSucceeds();
			await SenderFor(t, mode, "p").SendAsync(actions.Select((a, i) => (a, new Doc($"d{i}", "n", i))).ToArray());

			var expected = mode switch
			{
				BulkItemIdentity.Always => true,
				BulkItemIdentity.Never => false,
				_ => actions.Any(NeedsIdentity)
			};
			Asked(t.Requests.Single()).Should().Be(expected);
		}, iter: 300);
	}

	[Test]
	public async Task EveryPositionReportsTheServersIdAndIndexWhenAsked()
	{
		await Gen.Select(AnyAction.List[1, 20], Gen.OneOfConst(BulkItemIdentity.Auto, BulkItemIdentity.Always)).SampleAsync(async x =>
		{
			var (actions, mode) = x;
			var t = ScriptedTransport.AlwaysSucceeds();
			var response = await SenderFor(t, mode, "p").SendAsync(actions.Select((a, i) => (a, new Doc($"d{i}", "n", i))).ToArray());

			var asked = Asked(t.Requests.Single());
			var items = response.Items.ToArray();
			items.Should().HaveCount(actions.Count);
			for (var i = 0; i < actions.Count; i++)
			{
				var a = actions[i];
				if (!asked)
				{
					items[i].Id.Should().BeNull();
					items[i].Index.Should().BeNull();
					continue;
				}
				items[i].Id.Should().Be(string.IsNullOrWhiteSpace(a.Id) ? $"gen-0-{i}" : a.Id);
				items[i].Index.Should().Be((a.IndexName ?? "p") + (a.RequireAlias ? "-000001" : ""));
			}
		}, iter: 300);
	}

	[Test]
	public async Task IdentityIsCorrectPerPositionThroughRetriesOfAnyShape()
	{
		var statuses = Gen.OneOfConst(201, 201, 400, 503);
		await Gen.Select(Gen.Int[1, 20], Gen.Int[0, 3]).SelectMany(x => statuses.Array[(x.Item2 + 1) * x.Item1].Select(flat => (n: x.Item1, retries: x.Item2, flat))).SampleAsync(async x =>
		{
			var (n, retries, flat) = x;
			int StatusFor(int attempt, int orig) => flat[attempt * n + orig];

			// the script cannot know the original position of a re-sent item from the request alone, so every body carries it (N = position + 1)
			var t = new ScriptedTransport((attempt, r) => ScriptedResponse.Items(r.Lines.Where((_, i) => i % 2 == 1)
				.Select(l => StatusFor(attempt, JsonDocument.Parse(l).RootElement.GetProperty("N").GetInt32() - 1)).ToArray()));

			var docs = Enumerable.Range(0, n).Select(i => (BulkAction.Index(), new Doc("x", "n", i + 1))).ToArray();
			var response = await SenderFor(t, target: "p", retry: NoDelay with { MaxRetries = retries }).SendAsync(docs);

			// reference model: the id of a position comes from the last request that carried it
			var lastId = new string[n];
			var remaining = Enumerable.Range(0, n).ToList();
			for (var attempt = 0; attempt <= retries && remaining.Count > 0; attempt++)
			{
				for (var j = 0; j < remaining.Count; j++) lastId[remaining[j]] = $"gen-{attempt}-{j}";
				remaining = remaining.Where(i => StatusFor(attempt, i) == 503).ToList();
			}

			t.Requests.Should().OnlyContain(r => Asked(r));
			response.Items.Select(i => i.Id).Should().Equal(lastId);
			response.Items.Should().OnlyContain(i => i.Index == "p");
		}, iter: 300);
	}
}

internal static class BulkItemIdentityTestExtensions
{
	/// <summary>Re-creates a sender over plain documents with the same transport behaviour, for the IngestAll tests.</summary>
	public static BulkSender<(BulkAction, Doc), Doc> AsDocSender(this BulkSender<(BulkAction, Doc), Doc> sender) => sender;
}
