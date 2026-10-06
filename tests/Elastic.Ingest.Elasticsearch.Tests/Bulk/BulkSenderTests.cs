// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.Bulk;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests.Bulk;

public class BulkSenderTests
{
	private static readonly BulkRetryPolicy FastRetry = BulkRetryPolicy.Default with
	{
		MaxRetries = 3,
		Backoff = static _ => TimeSpan.Zero
	};

	private static BulkSender<Doc, Doc> Sender(ScriptedTransport t, Func<Doc, BulkAction> action = null, string target = null, BulkRetryPolicy retry = null) =>
		BulkSender.Create(t.Transport, BulkTestContext.Default.Doc, action ?? (static d => BulkAction.Index(d.Id)), target, retry);

	private static Doc[] Docs(int n) => Enumerable.Range(0, n).Select(i => new Doc($"id{i}", $"name{i}", i)).ToArray();

	[Test]
	public async Task SendsToBulkWithoutTargetAndWithFilterPath()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var response = await Sender(t).SendAsync(Docs(2));

		response.ApiCallDetails.HasSuccessfulStatusCode.Should().BeTrue();
		response.Items.Should().HaveCount(2);
		response.Errors.Should().BeFalse();
		t.Requests.Should().ContainSingle().Which.PathAndQuery.Should().StartWith("_bulk?filter_path=errors,");
	}

	[Test]
	public async Task TargetIsPrefixedToTheBulkUrl()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		await Sender(t, target: "products").SendAsync(Docs(1));
		t.Requests.Single().PathAndQuery.Should().StartWith("products/_bulk?");
	}

	[Test]
	public async Task WritesGoldenNdjsonForEveryAction()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var templates = new Dictionary<string, string> { ["a"] = "b" };
		var actions = new[]
		{
			BulkAction.Index("1", "idx"),
			BulkAction.Index(),
			BulkAction.Create("2").WithRequireAlias(),
			BulkAction.Create("3", "idx").WithDynamicTemplates(templates),
			BulkAction.Update("4", "idx"),
			BulkAction.Delete("5", "idx"),
			BulkAction.ScriptedHashUpsert("6", new HashedBulkUpdate("hash", "abc") { Parameters = new Dictionary<string, string> { ["p"] = "v" } }),
		};
		var i = 0;
		var sender = Sender(t, _ => actions[i++]);
		await sender.SendAsync(new Doc[actions.Length].Select((_, n) => new Doc("x", "n", n)).ToArray());

		t.Requests.Single().BodyText.Should().Be(string.Join("",
			"{\"index\":{\"_index\":\"idx\",\"_id\":\"1\"}}\n{\"Id\":\"x\",\"Name\":\"n\",\"N\":0}\n",
			"{\"index\":{}}\n{\"Id\":\"x\",\"Name\":\"n\",\"N\":1}\n",
			"{\"create\":{\"_id\":\"2\",\"require_alias\":true}}\n{\"Id\":\"x\",\"Name\":\"n\",\"N\":2}\n",
			"{\"create\":{\"_index\":\"idx\",\"_id\":\"3\",\"dynamic_templates\":{\"a\":\"b\"}}}\n{\"Id\":\"x\",\"Name\":\"n\",\"N\":3}\n",
			"{\"update\":{\"_index\":\"idx\",\"_id\":\"4\"}}\n{\"doc_as_upsert\": true, \"doc\": {\"Id\":\"x\",\"Name\":\"n\",\"N\":4} }\n",
			"{\"delete\":{\"_index\":\"idx\",\"_id\":\"5\"}}\n",
			"{\"update\":{\"_id\":\"6\"}}\n{ \"scripted_upsert\": true, \"upsert\": {}, \"script\": { \"source\": \"if (ctx._source.hash == params.hash ) { ctx.op = 'noop' } else { ctx._source = params.doc; ctx._source.hash = params.hash } \", \"params\": { \"hash\": \"abc\", \"p\": \"v\", \"doc\":{\"Id\":\"x\",\"Name\":\"n\",\"N\":6} } } }\n"));
	}

	[Test]
	public async Task DeleteWritesNoBodyLine()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var bodyCalls = 0;
		var sender = new BulkSender<Doc, Doc>(new BulkSenderOptions<Doc, Doc>
		{
			Transport = t.Transport,
			BodyTypeInfo = BulkTestContext.Default.Doc,
			Action = static d => BulkAction.Delete(d.Id),
			Body = d => { bodyCalls++; return d; }
		});
		await sender.SendAsync(Docs(3));

		t.Requests.Single().Lines.Should().HaveCount(3).And.OnlyContain(l => l.StartsWith("{\"delete\":"));
		bodyCalls.Should().Be(0);
	}

	[Test]
	public async Task SpanAndEnumerableOverloadsProduceTheSameBody()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var s = Sender(t);
		var docs = Docs(5);
		await s.SendAsync(docs.AsSpan());
		await s.SendAsync(docs.ToList());
		await s.SendAsync(docs.Select(d => d));
		await s.SendAsync(docs);
		t.Requests.Select(r => r.BodyText).Distinct().Should().ContainSingle();
	}

	[Test]
	public async Task NoRetryPolicySendsExactlyOneRequestEvenWhenItemsFail()
	{
		var t = new ScriptedTransport((_, _) => ScriptedResponse.Items(201, 429, 503));
		var response = await Sender(t).SendAsync(Docs(3));

		t.Requests.Should().ContainSingle();
		response.Items.Select(i => i.Status).Should().Equal(201, 429, 503);
	}

	[Test]
	public async Task RetryResendsOnlyFailedItemsAndKeepsItemsPositional()
	{
		var t = new ScriptedTransport((attempt, _) => attempt switch
		{
			0 => ScriptedResponse.Items(201, 429, 201, 503),
			1 => ScriptedResponse.Items(201, 503),
			_ => ScriptedResponse.Items(201)
		});
		var response = await Sender(t, retry: FastRetry).SendAsync(Docs(4));

		t.Requests.Select(r => r.Lines.Length / 2).Should().Equal(4, 2, 1);
		IdsOf(t.Requests[1]).Should().Equal("id1", "id3");
		IdsOf(t.Requests[2]).Should().Equal("id3");
		response.Items.Select(i => i.Status).Should().Equal(201, 201, 201, 201);
	}

	[Test]
	public async Task ExhaustedRetriesReturnTheLastResultPerPosition()
	{
		var t = new ScriptedTransport((attempt, r) =>
			ScriptedResponse.Items(Enumerable.Repeat(attempt == 0 ? 429 : 503, ScriptedTransport.CountOperations(r)).ToArray()));
		var response = await Sender(t, retry: FastRetry with { MaxRetries = 2 }).SendAsync(Docs(3));

		t.Requests.Should().HaveCount(3);
		response.Items.Select(i => i.Status).Should().Equal(503, 503, 503);
	}

	[Test]
	public async Task NonRetryableFailuresAreNotResent()
	{
		var t = new ScriptedTransport((attempt, _) => attempt == 0 ? ScriptedResponse.Items(400, 409, 429) : ScriptedResponse.Items(201));
		var response = await Sender(t, retry: FastRetry).SendAsync(Docs(3));

		IdsOf(t.Requests[1]).Should().Equal("id2");
		response.Items.Select(i => i.Status).Should().Equal(400, 409, 201);
	}

	[Test]
	public async Task Http429ResendsTheWholeRequest()
	{
		var t = new ScriptedTransport((attempt, r) => attempt == 0
			? ScriptedResponse.Http(429)
			: ScriptedResponse.Items(Enumerable.Repeat(201, ScriptedTransport.CountOperations(r)).ToArray()));
		var response = await Sender(t, retry: FastRetry).SendAsync(Docs(3));

		t.Requests.Should().HaveCount(2);
		t.Requests[1].BodyText.Should().Be(t.Requests[0].BodyText);
		response.Items.Should().HaveCount(3);
	}

	[Test]
	public async Task OtherHttpFailuresAreNotRetried()
	{
		var t = new ScriptedTransport((_, _) => ScriptedResponse.Http(500));
		var response = await Sender(t, retry: FastRetry).SendAsync(Docs(3));

		t.Requests.Should().ContainSingle();
		response.ApiCallDetails.HasSuccessfulStatusCode.Should().BeFalse();
	}

	[Test]
	public async Task HttpFailureOnRetryKeepsEarlierPositionalResults()
	{
		var t = new ScriptedTransport((attempt, _) => attempt == 0 ? ScriptedResponse.Items(201, 503) : ScriptedResponse.Http(500));
		var response = await Sender(t, retry: FastRetry).SendAsync(Docs(2));

		t.Requests.Should().HaveCount(2);
		response.Items.Select(i => i.Status).Should().Equal(201, 503);
	}

	[Test]
	public async Task CustomClassifierDecidesWhatIsRetried()
	{
		var t = new ScriptedTransport((attempt, _) => attempt == 0 ? ScriptedResponse.Items(409, 429) : ScriptedResponse.Items(201));
		var policy = FastRetry with { IsRetryable = static i => i.Status == 409 };
		var response = await Sender(t, retry: policy).SendAsync(Docs(2));

		IdsOf(t.Requests[1]).Should().Equal("id0");
		response.Items.Select(i => i.Status).Should().Equal(201, 429);
	}

	[Test]
	public async Task ClassifierIsNotCalledWhenServerReportsNoErrors()
	{
		var calls = 0;
		var t = ScriptedTransport.AlwaysSucceeds();
		var policy = FastRetry with { IsRetryable = _ => { calls++; return true; } };
		await Sender(t, retry: policy).SendAsync(Docs(10));
		calls.Should().Be(0);
	}

	[Test]
	public async Task EmptyBatchStillProducesAValidRequest()
	{
		var t = new ScriptedTransport((_, _) => ScriptedResponse.Items());
		var response = await Sender(t).SendAsync(Array.Empty<Doc>());
		response.Items.Should().BeEmpty();
		t.Requests.Single().Body.Should().BeEmpty();
	}

	[Test]
	public async Task LargeBatchGrowsTheBufferWithoutCorruption()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var docs = Enumerable.Range(0, 3000).Select(i => new Doc($"id{i}", new string('x', 2000) + i, i)).ToArray();
		await Sender(t).SendAsync(docs);

		var lines = t.Requests.Single().Lines;
		lines.Should().HaveCount(6000);
		for (var i = 0; i < 3000; i++)
			JsonDocument.Parse(lines[i * 2 + 1]).RootElement.GetProperty("N").GetInt32().Should().Be(i);
	}

	[Test]
	public async Task ThrowingBodyFailsTheCallAndLeavesTheSenderUsable()
	{
		var t = ScriptedTransport.AlwaysSucceeds();
		var fail = true;
		var sender = new BulkSender<Doc, Doc>(new BulkSenderOptions<Doc, Doc>
		{
			Transport = t.Transport,
			BodyTypeInfo = BulkTestContext.Default.Doc,
			Action = static d => BulkAction.Index(d.Id),
			Body = d => fail ? throw new InvalidOperationException("boom") : d
		});

		Action act = () => _ = sender.SendAsync(Docs(2));
		act.Should().Throw<InvalidOperationException>();
		t.Requests.Should().BeEmpty();

		fail = false;
		(await sender.SendAsync(Docs(2))).Items.Should().HaveCount(2);
	}

	[Test]
	public async Task ResponseItemsForSuccessAreSharedAndErrorsAreNot()
	{
		var t = new ScriptedTransport((_, _) => ScriptedResponse.Items(201, 201, 400, 400));
		var response = await Sender(t).SendAsync(Docs(4));
		var items = response.Items.ToArray();

		items[0].Should().BeSameAs(items[1]);
		items[2].Should().NotBeSameAs(items[3]);
		items[2].Error.Should().NotBeNull();
	}

	private static string[] IdsOf(CapturedRequest request) =>
		request.Lines.Where((_, i) => i % 2 == 0)
			.Select(l => JsonDocument.Parse(l).RootElement.EnumerateObject().First().Value.GetProperty("_id").GetString())
			.ToArray();
}
