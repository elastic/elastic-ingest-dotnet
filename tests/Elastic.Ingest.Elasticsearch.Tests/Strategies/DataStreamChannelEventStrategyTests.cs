// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Transport;
using FluentAssertions;
using Xunit;

namespace Elastic.Ingest.Elasticsearch.Tests.Strategies;

internal class TestDataStreamChannel(DataStreamChannelOptions<DataStreamDocument> options)
	: DataStreamChannel<DataStreamDocument>(options)
{
	public List<TrackStrategy> Strategies { get; } = new();

	protected override (HeaderSerializationStrategy, BulkHeader?) EventIndexStrategy(DataStreamDocument @event)
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

public class DataStreamChannelEventStrategyTests : ChannelTestWithSingleDocResponseBase
{
	[Fact]
	public void EmitsExpectedStrategies()
	{
		ApiCallDetails callDetails = null;

		var wait = new ManualResetEvent(false);

		Exception exception = null;
		using var channel = new TestDataStreamChannel(new DataStreamChannelOptions<DataStreamDocument>(Transport)
		{
			BufferOptions = new() { OutboundBufferMaxSize = 3 },
			DynamicTemplateLookup = document => document.Id == 2 ? new Dictionary<string, string> { { "id", "1" } } : null,
			ListExecutedPipelines = document => document.Id == 3,
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

		channel.TryWrite(new DataStreamDocument()); //0
		channel.TryWrite(new DataStreamDocument()); //1
		channel.TryWrite(new DataStreamDocument()); //2
		var signalled = wait.WaitOne(TimeSpan.FromSeconds(5));
		signalled.Should().BeTrue("because ExportResponseCallback should have been called");
		exception.Should().BeNull();

		callDetails.Uri.AbsolutePath.Should().Be("/datastreamdocument-generic-default/_bulk");


		channel.Strategies.Should().HaveCount(3);
		var strategies = channel.Strategies.OrderBy(s => s.Id).ToList();

		strategies[0].Strategy.Should().Be(HeaderSerializationStrategy.CreateNoParams);
		strategies[0].Header.Should().BeNull();

		strategies[1].Strategy.Should().Be(HeaderSerializationStrategy.Create);
		strategies[1].Header.Should().NotBeNull();
		strategies[1].Header!.Value.DynamicTemplates.Should().NotBeNull();

		strategies[2].Strategy.Should().Be(HeaderSerializationStrategy.Create);
		strategies[2].Header.Should().NotBeNull();
		strategies[2].Header!.Value.ListExecutedPipelines.Should().BeTrue();

	}
}
