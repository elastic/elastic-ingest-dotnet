// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.IO;
using System.Threading;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Transport;
using FluentAssertions;
using Xunit;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class DataStreamChannelTests : ChannelTestWithSingleDocResponseBase
{
	[Fact]
	public void DataStreamChannel_UsesCorrectUrlAndOperationHeader()
	{
		ApiCallDetails callDetails = null;

		var wait = new ManualResetEvent(false);

		using var channel = new DataStreamChannel<TestDocument>(new DataStreamChannelOptions<TestDocument>(Transport)
		{
			BufferOptions = new()
			{
				OutboundBufferMaxSize = 1
			},
			DataStream = new("type"),
			ExportResponseCallback = (response, _) =>
			{
				callDetails = response.ApiCallDetails;
				wait.Set();
			},
		});

		channel.TryWrite(new TestDocument());
		wait.WaitOne();

		callDetails.Uri.AbsolutePath.Should().Be("/type-generic-default/_bulk");

		var stream = new MemoryStream(callDetails.RequestBodyInBytes);
		var sr = new StreamReader(stream);
		var operation = sr.ReadLine();
		operation.Should().Be("{\"create\":{}}");
	}
}
