// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections;
using System.Collections.Generic;
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
		ExecuteAndAssert("/_bulk", "{\"create\":{\"_index\":\"testdocument-2023.07.29\"}}");

	[Fact]
	public void IndexChannel_UsesOperationAndId() =>
		ExecuteAndAssert("/_bulk", "{\"index\":{\"_index\":\"dotnet-2023.07.29\",\"_id\":\"mydocid\"}}", id: "mydocid", operationMode: OperationMode.Index);

	[Fact]
	public void IndexChannel_WritesCorrectHeaderWithAllOptions() =>
	ExecuteAndAssert("/fixed-index/_bulk", "{\"create\":{\"_id\":\"mydocid\",\"require_alias\":true,\"list_executed_pipelines\":true,\"dynamic_templates\":[{\"key1\":\"value1\"}]}}", "fixed-index", id: "mydocid", operationMode: OperationMode.Create,
		requiresAlias: true, listExecutedPipelines: true, dynamicTemplates: new Dictionary<string, string> { { "key1", "value1"} });

	private void ExecuteAndAssert(string expectedUrl, string expectedOperationHeader,
		string indexName = null, string id = null, OperationMode? operationMode = null, bool? requiresAlias = null,
		bool? listExecutedPipelines = null, IDictionary<string, string> dynamicTemplates = null)
	{
		ApiCallDetails callDetails = null;

		var wait = new ManualResetEvent(false);

		Exception exception = null;
		var options = new IndexChannelOptions<TestDocument>(Transport)
		{
			BufferOptions = new()
			{
				OutboundBufferMaxSize = 1
			},
			ExportExceptionCallback = e =>
			{
				exception = e;
				wait.Set();
			},
			ExportResponseCallback = (response, _) =>
			{
				callDetails = response.ApiCallDetails;
				wait.Set();
			},
			TimestampLookup = _ => new DateTimeOffset(2023, 07, 29, 20, 00, 00, TimeSpan.Zero),
		};

		if (operationMode.HasValue)
			options.OperationMode = operationMode.Value;

		if (!string.IsNullOrEmpty(id))
			options.BulkOperationIdLookup = _ => id;

		if (requiresAlias.HasValue)
			options.RequireAlias = _ => requiresAlias.Value;

		if (dynamicTemplates is not null)
			options.DynamicTemplateLookup = _ => dynamicTemplates;

		if (listExecutedPipelines.HasValue)
			options.ListExecutedPipelines = _ => listExecutedPipelines.Value;

		if (indexName is not null)
		{
			options.IndexFormat = indexName;
		}

		using var channel = new IndexChannel<TestDocument>(options);

		channel.TryWrite(new TestDocument());
		var signalled = wait.WaitOne(TimeSpan.FromSeconds(5));
		signalled.Should().BeTrue("because ExportResponseCallback should have been called");
		exception.Should().BeNull();

		callDetails.Should().NotBeNull();
		callDetails.Uri.AbsolutePath.Should().Be(expectedUrl);

		var stream = new MemoryStream(callDetails.RequestBodyInBytes);
		var sr = new StreamReader(stream);
		var operation = sr.ReadLine();
		operation.Should().Be(expectedOperationHeader);
	}
}
