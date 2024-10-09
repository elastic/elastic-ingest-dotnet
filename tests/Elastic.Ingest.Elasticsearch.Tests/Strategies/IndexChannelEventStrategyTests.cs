// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Transport;
using FluentAssertions;
using Xunit;

namespace Elastic.Ingest.Elasticsearch.Tests.Strategies;

internal class TestIndexChannel(IndexChannelOptions<IndexDocument> options) : IndexChannel<IndexDocument>(options)
{
	public List<TrackStrategy> Strategies { get; } = new();

	protected override (HeaderSerializationStrategy, BulkHeader?) EventIndexStrategy(IndexDocument @event)
	{
		var strategy = base.EventIndexStrategy(@event);
		Strategies.Add(new TrackStrategy
		{
			Id = @event.Id,
			Strategy = strategy.Item1,
			Header = strategy.Item2
		});
		return strategy;
	}
}

public class IndexChannelEventStrategyTests : ChannelTestWithSingleDocResponseBase
{
	[Fact]
	public void EmitsExpectedStrategies()
	{
		ApiCallDetails callDetails = null;

		var wait = new ManualResetEvent(false);

		Exception exception = null;
		using var channel = new TestIndexChannel(new IndexChannelOptions<IndexDocument>(Transport)
		{
			IndexFormat = "test-index",
			BufferOptions = new() { OutboundBufferMaxSize = 6 },
			DynamicTemplateLookup = document => document.Id == 2 ? new Dictionary<string, string> { { "id", "1" } } : null,
			BulkOperationIdLookup = document => document.Id == 3 ? "33" : null,
			BulkUpsertLookup = (document, _) => document.Id == 4,
			RequireAlias = document => document.Id == 5,
			ListExecutedPipelines = document => document.Id == 6,
			ExportExceptionCallback = e =>
			{
				exception = e;
				wait.Set();
			},
			ExportResponseCallback = (response, _) =>
			{
				callDetails = response.ApiCallDetails;
				wait.Set();
			}
		});

		channel.TryWrite(new IndexDocument()); //0
		channel.TryWrite(new IndexDocument()); //1
		channel.TryWrite(new IndexDocument()); //2
		channel.TryWrite(new IndexDocument()); //3
		channel.TryWrite(new IndexDocument()); //4
		channel.TryWrite(new IndexDocument()); //5
		var signalled = wait.WaitOne(TimeSpan.FromSeconds(5));
		signalled.Should().BeTrue("because ExportResponseCallback should have been called");
		exception.Should().BeNull();

		callDetails.Uri.AbsolutePath.Should().Be("/test-index/_bulk");

		channel.Strategies.Should().HaveCount(6);
		var strategies = channel.Strategies.OrderBy(s => s.Id).ToList();

		strategies[0].Id.Should().Be(1);
		strategies[0].Strategy.Should().Be(HeaderSerializationStrategy.CreateNoParams);
		strategies[0].Header.Should().BeNull();

		strategies[1].Strategy.Should().Be(HeaderSerializationStrategy.Create);
		strategies[1].Header.Should().NotBeNull();
		strategies[1].Header!.Value.DynamicTemplates.Should().NotBeNull();

		strategies[2].Strategy.Should().Be(HeaderSerializationStrategy.Create);
		strategies[2].Header.Should().NotBeNull();
		strategies[2].Header!.Value.Id.Should().Be("33");

		strategies[3].Strategy.Should().Be(HeaderSerializationStrategy.Update);

		strategies[4].Header!.Value.RequireAlias.Should().BeTrue();

		strategies[5].Header!.Value.ListExecutedPipelines.Should().BeTrue();

	}
}
