// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Elastic.Channels.Diagnostics;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Transport;
using FluentAssertions;
using Xunit;

namespace Elastic.Ingest.Elasticsearch.Tests.Strategies;

public class TestDocument
{
	private static int Counter = 0;
	private readonly int _id = ++Counter;

	public DateTimeOffset Timestamp { get; set; }
	public int Id => _id;
}

internal class TestIndexChannel(IndexChannelOptions<TestDocument> options) : IndexChannel<TestDocument>(options)
{
	public List<(HeaderSerializationStrategy, BulkHeader?)> Strategies { get; } = new();

	protected override (HeaderSerializationStrategy, BulkHeader?) EventIndexStrategy(TestDocument @event)
	{
		var strategy = base.EventIndexStrategy(@event);
		Strategies.Add(strategy);
		return strategy;
	}
}

public class IndexChannelEventOptionsTests : ChannelTestWithSingleDocResponseBase
{
	[Fact]
	public void DataStreamChannel_UsesCorrectUrlAndOperationHeader()
	{
		ApiCallDetails callDetails = null;

		var wait = new ManualResetEvent(false);

		Exception exception = null;
		using var channel = new TestIndexChannel(new IndexChannelOptions<TestDocument>(Transport)
		{
			IndexFormat = "test-index",
			BufferOptions = new() { OutboundBufferMaxSize = 1 },
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

		channel.TryWrite(new TestDocument()); //0
		channel.TryWrite(new TestDocument()); //1
		channel.TryWrite(new TestDocument()); //2
		channel.TryWrite(new TestDocument()); //3
		channel.TryWrite(new TestDocument()); //4
		channel.TryWrite(new TestDocument()); //5
		var signalled = wait.WaitOne(TimeSpan.FromSeconds(5));
		signalled.Should().BeTrue("because ExportResponseCallback should have been called");
		exception.Should().BeNull();

		callDetails.Uri.AbsolutePath.Should().Be("/test-index/_bulk");


		channel.Strategies.Should().HaveCount(6);

		channel.Strategies[0].Item1.Should().Be(HeaderSerializationStrategy.CreateNoParams);
		channel.Strategies[0].Item2.Should().BeNull();

		channel.Strategies[1].Item1.Should().Be(HeaderSerializationStrategy.Create);
		channel.Strategies[1].Item2.Should().NotBeNull();
		channel.Strategies[1].Item2!.Value.DynamicTemplates.Should().NotBeNull();

		channel.Strategies[2].Item1.Should().Be(HeaderSerializationStrategy.Create);
		channel.Strategies[2].Item2.Should().NotBeNull();
		channel.Strategies[2].Item2!.Value.Id.Should().Be("33");

		channel.Strategies[3].Item1.Should().Be(HeaderSerializationStrategy.Update);

		channel.Strategies[4].Item2!.Value.RequireAlias.Should().BeTrue();

		channel.Strategies[5].Item2!.Value.ListExecutedPipelines.Should().BeTrue();

	}
}
