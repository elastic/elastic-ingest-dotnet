// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests;

/// <summary>
/// Tests for <see cref="IngestChannelBase{TDocument,TChannelOptions}.DirectWriteAsync(System.Collections.Generic.IReadOnlyList{TDocument},CancellationToken)"/>.
/// DirectWrite bypasses all channel buffering and writes directly to Elasticsearch via _bulk.
/// </summary>
public class DirectWriteTests
{
	[Test]
	public async Task IndexChannelDirectWriteSingleDocumentSendsBulkRequest()
	{
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201))
		);

		using var channel = new IndexChannel<TestDocument>(new IndexChannelOptions<TestDocument>(client)
		{
			IndexFormat = "my-index",
		});

		var response = await channel.DirectWriteAsync(new TestDocument { Timestamp = DateTimeOffset.UtcNow });

		response.Should().NotBeNull();
		response.ApiCallDetails.HasSuccessfulStatusCode.Should().BeTrue();
		response.ApiCallDetails.Uri.AbsolutePath.Should().Be("/my-index/_bulk");
		response.Items.Should().HaveCount(1);
		response.Items.First().Status.Should().Be(201);
	}

	[Test]
	public async Task IndexChannelDirectWriteMultipleDocumentsSendsBulkRequest()
	{
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201, 201, 201))
		);

		using var channel = new IndexChannel<TestDocument>(new IndexChannelOptions<TestDocument>(client)
		{
			IndexFormat = "my-index",
		});

		var docs = new[]
		{
			new TestDocument { Timestamp = DateTimeOffset.UtcNow },
			new TestDocument { Timestamp = DateTimeOffset.UtcNow },
			new TestDocument { Timestamp = DateTimeOffset.UtcNow },
		};

		var response = await channel.DirectWriteAsync(docs);

		response.Should().NotBeNull();
		response.ApiCallDetails.HasSuccessfulStatusCode.Should().BeTrue();
		response.Items.Should().HaveCount(3);
		response.Items.Should().OnlyContain(i => i.Status == 201);
	}

	[Test]
	public async Task IndexChannelDirectWriteUsesCorrectOperationHeader()
	{
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201))
		);

		using var channel = new IndexChannel<TestDocument>(new IndexChannelOptions<TestDocument>(client)
		{
			IndexFormat = "fixed-index",
		});

		var response = await channel.DirectWriteAsync(new TestDocument { Timestamp = DateTimeOffset.UtcNow });

		var body = response.ApiCallDetails.RequestBodyInBytes;
		body.Should().NotBeNull("EnableDebugMode captures request body");

		var stream = new MemoryStream(body);
		var sr = new StreamReader(stream);
		var operation = sr.ReadLine();
		operation.Should().Be("{\"create\":{}}");
	}

	[Test]
	public async Task DataStreamChannelDirectWriteSendsBulkRequest()
	{
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201, 201))
		);

		using var channel = new DataStreamChannel<TestDocument>(new DataStreamChannelOptions<TestDocument>(client)
		{
			DataStream = new DataStreamName("logs"),
		});

		var docs = new[]
		{
			new TestDocument { Timestamp = DateTimeOffset.UtcNow },
			new TestDocument { Timestamp = DateTimeOffset.UtcNow },
		};

		var response = await channel.DirectWriteAsync(docs);

		response.Should().NotBeNull();
		response.ApiCallDetails.HasSuccessfulStatusCode.Should().BeTrue();
		response.ApiCallDetails.Uri.AbsolutePath.Should().Be("/logs-generic-default/_bulk");
		response.Items.Should().HaveCount(2);
	}

	[Test]
	public async Task DataStreamChannelDirectWriteUsesCreateOperationHeader()
	{
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201))
		);

		using var channel = new DataStreamChannel<TestDocument>(new DataStreamChannelOptions<TestDocument>(client)
		{
			DataStream = new DataStreamName("type"),
		});

		var response = await channel.DirectWriteAsync(new TestDocument());

		var body = response.ApiCallDetails.RequestBodyInBytes;
		var stream = new MemoryStream(body);
		var sr = new StreamReader(stream);
		var operation = sr.ReadLine();
		operation.Should().Be("{\"create\":{}}");
	}

	[Test]
	public async Task DirectWriteReturnsErrorResponseWhenBulkFails()
	{
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(400))
		);

		using var channel = new IndexChannel<TestDocument>(new IndexChannelOptions<TestDocument>(client)
		{
			IndexFormat = "my-index",
		});

		var response = await channel.DirectWriteAsync(new TestDocument { Timestamp = DateTimeOffset.UtcNow });

		response.Should().NotBeNull();
		response.Items.Should().HaveCount(1);
		response.Items.First().Status.Should().Be(400);
	}

	[Test]
	public async Task DirectWriteDoesNotAffectBufferedWrites()
	{
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201))
			.ClientCalls(c => c.BulkResponse(201))
		);

		var bufferedResponseReceived = new ManualResetEvent(false);

		using var channel = new IndexChannel<TestDocument>(new IndexChannelOptions<TestDocument>(client)
		{
			IndexFormat = "my-index",
			BufferOptions = new() { OutboundBufferMaxSize = 1 },
			ExportResponseCallback = (_, _) => bufferedResponseReceived.Set(),
		});

		// Direct write bypasses the buffer
		var directResponse = await channel.DirectWriteAsync(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		directResponse.ApiCallDetails.HasSuccessfulStatusCode.Should().BeTrue();

		// Buffered write still works independently
		channel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		bufferedResponseReceived.WaitOne(TimeSpan.FromSeconds(10)).Should().BeTrue("buffered write should still complete");
	}

	[Test]
	public async Task DirectWriteSupportsCancellation()
	{
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201))
		);

		using var channel = new IndexChannel<TestDocument>(new IndexChannelOptions<TestDocument>(client)
		{
			IndexFormat = "my-index",
		});

		using var cts = new CancellationTokenSource();
		cts.Cancel();

		Exception caught = null;
		try
		{
			await channel.DirectWriteAsync(
				new[] { new TestDocument { Timestamp = DateTimeOffset.UtcNow } }, cts.Token);
		}
		catch (Exception ex)
		{
			caught = ex;
		}

		caught.Should().NotBeNull("a cancelled token should prevent the request from completing");
		(caught is OperationCanceledException || caught is UnexpectedTransportException)
			.Should().BeTrue($"expected cancellation-related exception but got {caught.GetType().Name}");
	}

	[Test]
	public async Task DirectWriteParamsOverloadWorksForSingleDocument()
	{
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201))
		);

		using var channel = new IndexChannel<TestDocument>(new IndexChannelOptions<TestDocument>(client)
		{
			IndexFormat = "my-index",
		});

		var response = await channel.DirectWriteAsync(new TestDocument { Timestamp = DateTimeOffset.UtcNow });

		response.Should().NotBeNull();
		response.ApiCallDetails.HasSuccessfulStatusCode.Should().BeTrue();
		response.Items.Should().HaveCount(1);
	}

	[Test]
	public async Task DirectWriteParamsOverloadWorksForMultipleDocuments()
	{
		var client = TestSetup.CreateClient(v => v
			.ClientCalls(c => c.BulkResponse(201, 201))
		);

		using var channel = new IndexChannel<TestDocument>(new IndexChannelOptions<TestDocument>(client)
		{
			IndexFormat = "my-index",
		});

		var response = await channel.DirectWriteAsync(
			new TestDocument { Timestamp = DateTimeOffset.UtcNow },
			new TestDocument { Timestamp = DateTimeOffset.UtcNow }
		);

		response.Should().NotBeNull();
		response.Items.Should().HaveCount(2);
	}
}
