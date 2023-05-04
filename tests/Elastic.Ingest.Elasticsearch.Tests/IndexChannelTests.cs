// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.IO;
using System.Threading;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Transport;
using FluentAssertions;
using Xunit;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class IndexChannelTests : ChannelTestWithSingleDocResponseBase
{
	[Fact]
	public void IndexChannel_WithFixedIndexName_UsesCorrectUrlAndOperationHeader() =>
		ExecuteAndAssert("/fixed-index/_bulk", "{\"create\":{}}", "fixed-index");

	[Fact]
	public void IndexChannel_WithDynamicIndexName_UsesCorrectUrlAndOperationHeader() =>
		ExecuteAndAssert("/_bulk", "{\"create\":{\"_index\":\"dotnet-2023.07.29\"}}");

	private static void ExecuteAndAssert(string expectedUrl, string expectedOperationHeader, string indexName = null)
	{
		ApiCallDetails callDetails = null;

		var wait = new ManualResetEvent(false);

		var options = new IndexChannelOptions<TestDocument>(_transport)
		{
			BufferOptions = new()
			{
				OutboundBufferMaxSize = 1
			},
			ExportResponseCallback = (response, _) =>
			{
				callDetails = response.ApiCallDetails;
				wait.Set();
			},
			TimestampLookup = _ => new DateTimeOffset(2023, 07, 29, 20, 00, 00, TimeSpan.Zero),
		};

		if (indexName is not null)
		{
			options.IndexFormat = indexName;
		}

		using var channel = new IndexChannel<TestDocument>(options);

		channel.TryWrite(new TestDocument());
		wait.WaitOne();

		callDetails.Should().NotBeNull();
		callDetails.Uri.AbsolutePath.Should().Be(expectedUrl);

		var stream = new MemoryStream(callDetails.RequestBodyInBytes);
		var sr = new StreamReader(stream);
		var operation = sr.ReadLine();
		operation.Should().Be(expectedOperationHeader);
	}
}
