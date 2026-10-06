// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CsCheck;
using Elastic.Ingest.Elasticsearch.Bulk;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests.Bulk;

/// <summary>Property, model and specification tests that try to break the assumptions of <see cref="BulkSender{TItem,TBody}"/>.</summary>
public class BulkSenderPropertyTests
{
	private static readonly string[] Pieces =
	[
		"a", "Z", "0", " ", "-", "_", "\"", "\\", "/", "\n", "\t", "\r", "\u0000", "\u001f", "é", "日本語", "😀", "<", ">", "&", "'", "{", "}", ":", ","
	];

	private static readonly Gen<string> Text = Gen.OneOfConst(Pieces).Array[0, 8].Select(p => string.Concat(p));
	private static readonly Gen<string> NonBlank = Text.Select(s => "k" + s);
	private static readonly Gen<string> MaybeNull = Gen.Frequency((1, Gen.Const((string)null)), (4, NonBlank));
	private static readonly Gen<Doc> Docs = Gen.Select(NonBlank, Text, Gen.Int, (id, name, n) => new Doc(id, name, n));

	private static readonly Gen<BulkAction> Actions =
		Gen.Select(Gen.Int[0, 5], MaybeNull, MaybeNull, NonBlank, Gen.Bool, Gen.Bool, (kind, id, index, nonNullId, alias, templates) =>
		{
			BulkAction a = kind switch
			{
				0 => BulkAction.Index(id, index),
				1 => BulkAction.Create(id, index),
				2 => BulkAction.Update(nonNullId, index),
				3 => BulkAction.Delete(nonNullId, index),
				4 => BulkAction.ScriptedHashUpsert(nonNullId, new HashedBulkUpdate("f" + nonNullId.Length, "h" + nonNullId) { Parameters = new Dictionary<string, string> { [nonNullId] = id ?? "" } }, index),
				_ => BulkAction.Index(id, index)
			};
			if (alias) a = a.WithRequireAlias();
			if (templates && kind is 0 or 1) a = a.WithDynamicTemplates(new Dictionary<string, string> { [nonNullId] = id ?? "v" });
			return a;
		});

	private static BulkSender<(BulkAction, Doc), Doc> SenderFor(ScriptedTransport t, BulkRetryPolicy retry = null) =>
		new(new BulkSenderOptions<(BulkAction, Doc), Doc>
		{
			Transport = t.Transport,
			BodyTypeInfo = BulkTestContext.Default.Doc,
			Action = static x => x.Item1,
			Body = static x => x.Item2,
			Retry = retry ?? BulkRetryPolicy.None
		});

	[Test]
	public async Task OutputIsAlwaysWellFormedNdjson()
	{
		await Gen.Select(Actions, Docs).List[0, 25].SampleAsync(async items =>
		{
			var t = ScriptedTransport.AlwaysSucceeds();
			await SenderFor(t).SendAsync(items.ToArray());

			var body = t.Requests.Single().BodyText;
			if (items.Count == 0) { body.Should().BeEmpty(); return; }
			body.Should().EndWith("\n");

			var lines = body.Split('\n');
			// split leaves an empty last element after the trailing line feed
			var expectedLines = items.Sum(i => i.Item1.HasBody ? 2 : 1);
			lines.Length.Should().Be(expectedLines + 1);

			var cursor = 0;
			foreach (var (action, doc) in items)
			{
				var header = JsonDocument.Parse(lines[cursor++]).RootElement;
				var op = header.EnumerateObject().Single();
				op.Name.Should().Be(action.Kind switch
				{
					BulkActionKind.Index => "index",
					BulkActionKind.Create => "create",
					BulkActionKind.Delete => "delete",
					_ => "update"
				});

				if (!string.IsNullOrWhiteSpace(action.IndexName)) op.Value.GetProperty("_index").GetString().Should().Be(action.IndexName);
				else op.Value.TryGetProperty("_index", out _).Should().BeFalse();
				if (!string.IsNullOrWhiteSpace(action.Id)) op.Value.GetProperty("_id").GetString().Should().Be(action.Id);
				else op.Value.TryGetProperty("_id", out _).Should().BeFalse();

				if (!action.HasBody) continue;
				var line = JsonDocument.Parse(lines[cursor++]).RootElement;
				var source = action.Kind switch
				{
					BulkActionKind.Update => line.GetProperty("doc"),
					BulkActionKind.ScriptedHashUpsert => line.GetProperty("script").GetProperty("params").GetProperty("doc"),
					_ => line
				};
				source.GetProperty("Id").GetString().Should().Be(doc.Id);
				source.GetProperty("Name").GetString().Should().Be(doc.Name);
				source.GetProperty("N").GetInt32().Should().Be(doc.N);
			}
		}, iter: 300);
	}

	[Test]
	public async Task OutputIsByteIdenticalToTheLegacyWriter()
	{
		// The legacy writer does not support delete (it always writes a body line) so it is excluded from the model.
		// The legacy path serializes with DefaultIgnoreCondition.WhenWritingDefault while BulkSender follows the caller's
		// JsonTypeInfo, so keep default valued members (N == 0) out of this comparison.
		var legacyActions = Actions.Where(a => a.Kind != BulkActionKind.Delete);
		var nonDefaultDocs = Docs.Select(d => d with { N = d.N == 0 ? 1 : d.N });
		await Gen.Select(legacyActions, nonDefaultDocs).List[1, 20].SampleAsync(async items =>
		{
			var t = ScriptedTransport.AlwaysSucceeds();
			await SenderFor(t).SendAsync(items.ToArray());

			var legacyOptions = new IndexChannelOptions<Doc>(t.Transport) { SerializerContext = BulkTestContext.Default };
			var queue = new Queue<BulkAction>(items.Select(i => i.Item1));
			var page = items.Select(i => i.Item2).ToArray();
			// the legacy factory calls the header factory once per event, in order
			var expected = BulkRequestDataFactory.GetBytes(new ArraySegment<Doc>(page), legacyOptions, _ => ToLegacy(queue.Dequeue()));

			System.Text.Encoding.UTF8.GetString(t.Requests.Single().Body).Should().Be(System.Text.Encoding.UTF8.GetString(expected.Span),
				string.Join(", ", items.Select(i => $"{i.Item1.Kind}(alias={i.Item1.RequireAlias},tpl={i.Item1.DynamicTemplates?.Count})")));
		}, iter: 300);
	}

	[Test]
	public async Task RetriesMatchAReferenceModelPositionally()
	{
		var statuses = Gen.OneOfConst(201, 201, 400, 409, 429, 503);
		await Gen.Select(Gen.Int[1, 25], Gen.Int[0, 4]).SelectMany(x => statuses.Array[(x.Item2 + 1) * x.Item1].Select(flat => (n: x.Item1, retries: x.Item2, flat)))
			.SampleAsync(async x =>
		{
			var (n, retries, flat) = x;
			int StatusFor(int attempt, int orig) => flat[attempt * n + orig];

			var docs = Enumerable.Range(0, n).Select(i => new Doc($"id{i}", "n", i)).ToArray();
			var t = new ScriptedTransport((attempt, request) =>
			{
				var ids = IdsOf(request);
				return ScriptedResponse.Items(ids.Select(id => StatusFor(attempt, int.Parse(id[2..], System.Globalization.CultureInfo.InvariantCulture))).ToArray());
			});
			var sender = BulkSender.Create(t.Transport, BulkTestContext.Default.Doc, static d => BulkAction.Index(d.Id), null,
				BulkRetryPolicy.Default with { MaxRetries = retries, Backoff = static _ => TimeSpan.Zero });

			var response = await sender.SendAsync(docs);

			// reference model
			var expectedFinal = new int[n];
			var remaining = Enumerable.Range(0, n).ToList();
			var expectedRequests = new List<int[]>();
			for (var attempt = 0; attempt <= retries && remaining.Count > 0; attempt++)
			{
				expectedRequests.Add(remaining.ToArray());
				foreach (var i in remaining) expectedFinal[i] = StatusFor(attempt, i);
				remaining = remaining.Where(i => StatusFor(attempt, i) is 429 or 503).ToList();
			}

			t.Requests.Count.Should().Be(expectedRequests.Count);
			for (var a = 0; a < expectedRequests.Count; a++)
				IdsOf(t.Requests[a]).Should().Equal(expectedRequests[a].Select(i => $"id{i}"));
			response.Items.Select(i => i.Status).Should().Equal(expectedFinal);
		}, iter: 300);
	}

	[Test]
	public async Task NoRetryPolicyAlwaysSendsOneRequestAndReturnsServerItems()
	{
		await Gen.Int[1, 30].SelectMany(n => Gen.OneOfConst(201, 400, 429, 503).Array[n]).SampleAsync(async statuses =>
		{
			var t = new ScriptedTransport((_, _) => ScriptedResponse.Items(statuses));
			var docs = statuses.Select((_, i) => new Doc($"id{i}", "n", i)).ToArray();
			var response = await BulkSender.Create(t.Transport, BulkTestContext.Default.Doc, static d => BulkAction.Index(d.Id)).SendAsync(docs);

			t.Requests.Should().ContainSingle();
			response.Items.Select(i => i.Status).Should().Equal(statuses);
		}, iter: 200);
	}

	[Test]
	public async Task OneSharedSenderIsSafeAcrossThreads()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var sender = BulkSender.Create(t.Transport, BulkTestContext.Default.Doc, static d => BulkAction.Index(d.Id));

		var tasks = Enumerable.Range(0, 32).Select(w => Task.Run(async () =>
		{
			for (var round = 0; round < 20; round++)
			{
				var docs = Enumerable.Range(0, 40).Select(i => new Doc($"w{w}r{round}i{i}", new string('é', i), i)).ToArray();
				var response = await sender.SendAsync(docs);
				response.Items.Should().HaveCount(docs.Length);
			}
		})).ToArray();
		await Task.WhenAll(tasks);

		var seen = new HashSet<string>();
		foreach (var request in t.Requests)
		{
			var lines = request.Lines;
			lines.Length.Should().Be(80);
			for (var i = 0; i < lines.Length; i += 2)
			{
				var headerId = JsonDocument.Parse(lines[i]).RootElement.EnumerateObject().First().Value.GetProperty("_id").GetString();
				var bodyId = JsonDocument.Parse(lines[i + 1]).RootElement.GetProperty("Id").GetString();
				headerId.Should().Be(bodyId);
				seen.Add(bodyId).Should().BeTrue("every document is sent exactly once");
			}
		}
		seen.Should().HaveCount(32 * 20 * 40);
	}

	private static string[] IdsOf(CapturedRequest request) =>
		request.Lines.Where((_, i) => i % 2 == 0)
			.Select(l => JsonDocument.Parse(l).RootElement.EnumerateObject().First().Value.GetProperty("_id").GetString())
			.ToArray();

	private static BulkOperationHeader ToLegacy(BulkAction a) => a.Kind switch
	{
		BulkActionKind.Index => new IndexOperation { Index = a.IndexName, Id = a.Id, RequireAlias = a.RequireAlias ? true : null, DynamicTemplates = a.DynamicTemplates?.ToDictionary(k => k.Key, k => k.Value) },
		BulkActionKind.Create => new CreateOperation { Index = a.IndexName, Id = a.Id, RequireAlias = a.RequireAlias ? true : null, DynamicTemplates = a.DynamicTemplates?.ToDictionary(k => k.Key, k => k.Value) },
		BulkActionKind.Update => new UpdateOperation { Index = a.IndexName, Id = a.Id, RequireAlias = a.RequireAlias ? true : null },
		BulkActionKind.ScriptedHashUpsert => new ScriptedHashUpdateOperation { Index = a.IndexName, Id = a.Id, RequireAlias = a.RequireAlias ? true : null, UpdateInformation = a.HashUpdate },
		_ => throw new NotSupportedException()
	};
}
