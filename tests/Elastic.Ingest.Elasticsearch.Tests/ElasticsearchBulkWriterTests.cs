#if NET8_0_OR_GREATER // TODO - Support other targets

using Xunit;
using System.Text;
using System;
using Elastic.Transport;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using System.Text.Json.Serialization;
using System.IO;
using System.Threading;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class ElasticsearchBulkWriterTests
{
	private static readonly ITransport<ITransportConfiguration> Transport = new DistributedTransport<ITransportConfiguration>(
		new TransportConfigurationDescriptor(new SingleNodePool(new Uri("http://localhost:9200")),
				new InMemoryRequestInvoker(Encoding.UTF8.GetBytes("{\"items\":[{\"create\":{\"status\":201}}]}"), 201))
			.DisablePing()
			.EnableDebugMode());

	private static readonly byte[] Payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new TestData { Name = "Testing" }));

	[Fact]
	public async Task IndexOperation_WithNoParameters_IsWrittenCorrectly()
	{
		var writer = Transport.GetElasticsearchBulkWriter("some-index");

		await writer.WriteIndexOperationAsync(Payload);
		await writer.WriteIndexOperationAsync(Payload);

		var response = await writer.CompleteAsync();

		response.ApiCallDetails.Uri.AbsolutePath.Should().Be("/some-index/_bulk");
		response.ApiCallDetails.Uri.Query.Should().Be("?filter_path=errors,took,error,items.*.status,items.*.error");

		var body = Encoding.UTF8.GetString(response.ApiCallDetails.RequestBodyInBytes);

		body.Should().Be("""
			{"index":{}}
			{"name":"Testing"}
			{"index":{}}
			{"name":"Testing"}
			
			""");
	}

	[Fact]
	public async Task IndexOperation_WithIdParameter_IsWrittenCorrectly()
	{
		var writer = Transport.GetElasticsearchBulkWriter("some-index");

		await writer.WriteIndexOperationAsync(Payload);
		await writer.WriteIndexOperationAsync(Payload, Id.From("my-unique-id"));

		var response = await writer.CompleteAsync();

		response.ApiCallDetails.Uri.AbsolutePath.Should().Be("/some-index/_bulk");
		response.ApiCallDetails.Uri.Query.Should().Be("?filter_path=errors,took,error,items.*.status,items.*.error");

		var body = Encoding.UTF8.GetString(response.ApiCallDetails.RequestBodyInBytes);

		body.Should().Be("""
			{"index":{}}
			{"name":"Testing"}
			{"index":{"_id":"my-unique-id"}}
			{"name":"Testing"}
			
			""");
	}

	[Fact]
	public async Task IndexOperation_WithTargetParameter_IsWrittenCorrectly()
	{
		var writer = Transport.GetElasticsearchBulkWriter();

		await writer.WriteIndexOperationAsync(Payload);
		await writer.WriteIndexOperationAsync(Payload, Target.From("my-custom-index"));

		var response = await writer.CompleteAsync();

		response.ApiCallDetails.Uri.AbsolutePath.Should().Be("/_bulk");
		response.ApiCallDetails.Uri.Query.Should().Be("?filter_path=errors,took,error,items.*.status,items.*.error");

		var body = Encoding.UTF8.GetString(response.ApiCallDetails.RequestBodyInBytes);

		body.Should().Be("""
			{"index":{}}
			{"name":"Testing"}
			{"index":{"_index":"my-custom-index"}}
			{"name":"Testing"}
			
			""");
	}

	[Fact]
	public async Task IndexOperation_WithAllParameters_IsWrittenCorrectly()
	{
		var writer = Transport.GetElasticsearchBulkWriter();

		await writer.WriteIndexOperationAsync(Payload, new BulkIndexOptions
		{
			Id = "my-id",
			Target = "my-index",
			RequireAlias = true,
			ListExecutedPipelines = true,
			DynamicTemplateMappings = new Dictionary<string, string> { { "item1", "value1" }, { "item2", "value2" } }
		});

		var response = await writer.CompleteAsync();

		response.ApiCallDetails.Uri.AbsolutePath.Should().Be("/_bulk");
		response.ApiCallDetails.Uri.Query.Should().Be("?filter_path=errors,took,error,items.*.status,items.*.error");

		var body = Encoding.UTF8.GetString(response.ApiCallDetails.RequestBodyInBytes);

		body.Should().Be("""
			{"index":{"_id":"my-id","_index":"my-index","require_alias":true,"list_executed_pipelines":true,"dynamic_templates":{"item1":"value1","item2":"value2"}}}
			{"name":"Testing"}
			
			""");
	}

	[Fact]
	public async Task IndexOperation_WithFalseParameters_IsWrittenCorrectly()
	{
		var writer = Transport.GetElasticsearchBulkWriter();

		await writer.WriteIndexOperationAsync(Payload, new BulkIndexOptions
		{
			Id = "my-id",
			Target = "my-index",
			RequireAlias = false,
			ListExecutedPipelines = false,
			DynamicTemplateMappings = new Dictionary<string, string> { { "item1", "value1" }, { "item2", "value2" } }
		});

		var response = await writer.CompleteAsync();

		response.ApiCallDetails.Uri.AbsolutePath.Should().Be("/_bulk");
		response.ApiCallDetails.Uri.Query.Should().Be("?filter_path=errors,took,error,items.*.status,items.*.error");

		var body = Encoding.UTF8.GetString(response.ApiCallDetails.RequestBodyInBytes);

		body.Should().Be("""
			{"index":{"_id":"my-id","_index":"my-index","dynamic_templates":{"item1":"value1","item2":"value2"}}}
			{"name":"Testing"}
			
			""");
	}

	[Fact]
	public async Task IndexOperation_WithEmptyDynamicTemplateParameter_IsWrittenCorrectly()
	{
		var writer = Transport.GetElasticsearchBulkWriter();

		await writer.WriteIndexOperationAsync(Payload, new BulkIndexOptions
		{
			Id = "my-id",
			Target = "my-index",
			RequireAlias = true,
			ListExecutedPipelines = false,
			DynamicTemplateMappings = DynamicTemplateMappings.Empty
		});

		var response = await writer.CompleteAsync();

		response.ApiCallDetails.Uri.AbsolutePath.Should().Be("/_bulk");
		response.ApiCallDetails.Uri.Query.Should().Be("?filter_path=errors,took,error,items.*.status,items.*.error");

		var body = Encoding.UTF8.GetString(response.ApiCallDetails.RequestBodyInBytes);

		body.Should().Be("""
			{"index":{"_id":"my-id","_index":"my-index","require_alias":true}}
			{"name":"Testing"}
			
			""");
	}

	public class TestData
	{
		[JsonPropertyName("name")]
		public string Name { get; set; }
	}
}

public class StreamingBulkResponseTests
{
	private static readonly byte[] ErrorsAndStatusWithError = Encoding.UTF8.GetBytes("{\"errors\":true,\"took\":20748119079,\"items\":[{\"index\":{\"status\":200}},{\"index\":{\"status\":200}},{\"index\":{\"status\":200}},{\"index\":{\"status\":200}},{\"create\":{\"status\":409,\"error\":{\"type\":\"version_conflict_engine_exception\",\"reason\":\"[1]: version conflict, document already exists (current version [2])\",\"index_uuid\":\"G_TQTDgDRZmbMjBNT-C_eg\",\"shard\":\"0\",\"index\":\"test1\"}}}]}");
	private static readonly byte[] ErrorsAndStatusNoErrors = Encoding.UTF8.GetBytes("{\"errors\":false,\"took\":20757446561,\"items\":[{\"index\":{\"status\":200}},{\"index\":{\"status\":200}}]}");
	private static readonly byte[] Error = Encoding.UTF8.GetBytes("{\"error\":{\"root_cause\":[{\"type\":\"x_content_parse_exception\",\"reason\":\"[1:3] [UpdateRequest] unknown field [field1]\"}],\"type\":\"x_content_parse_exception\",\"reason\":\"[1:3] [UpdateRequest] unknown field [field1]\"},\"status\":400}");

	private class BufferedMemoryStream(ReadOnlyMemory<byte> data, IReadOnlyList<int> lengths) : MemoryStream
	{
		private readonly ReadOnlyMemory<byte> _data = data;
		private readonly IReadOnlyList<int> _lengths = lengths;

		private int readCounter;
		private int position;

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			var readCount = readCounter++;

			if (position >= _data.Length)
				return ValueTask.FromResult(0);

			var lengthRequired = readCount < _lengths.Count ? _lengths[readCount] : -1;

			if (lengthRequired == -1 || lengthRequired > _data.Length - position)
			{
				_data.Span[position..].CopyTo(buffer.Span);
				var lengthBuffered = _data.Span[position..].Length;
				position = _data.Length;
				return ValueTask.FromResult(lengthBuffered);
			}

			_data.Span.Slice(position, lengthRequired).CopyTo(buffer.Span);

			position += lengthRequired;

			return ValueTask.FromResult(lengthRequired);
		}
	}

	[Fact]
	public async Task ErrorsAndStatus_ReturnsExpectedStatuses()
	{
		using var stream = new MemoryStream(ErrorsAndStatusNoErrors);

		using var response = new StreamingBulkResponse(stream, ResponseMode.ErrorsAndStatus);

		//using var response = await StreamingBulkResponse.CreateAsync(stream, ResponseMode.ErrorsAndStatus);

		// TODO - Initialise

		response.Errors.Should().BeFalse();
		response.Took.Should().Be(20757446561);

		var items = new List<OperationResult>();

		await foreach (var result in response.GetOperationResultsAsync())
		{
			items.Add(result);
		}

		items.Count.Should().Be(2);

		for (var i = 0; i < items.Count; i++)
		{
			items[i].OperationIndex.Should().Be(i);
		}
	}

	[Fact]
	public async Task ErrorsAndStatus_WithSmallFirstRead_ReturnsExpectedStatuses()
	{
		using var stream = new BufferedMemoryStream(ErrorsAndStatusNoErrors, [5, 5]);

		using var response = await StreamingBulkResponse.CreateAsync(stream, ResponseMode.ErrorsAndStatus);

		response.Errors.Should().BeFalse();
		response.Took.Should().Be(20757446561);

		var items = new List<OperationResult>();

		await foreach (var result in response.GetOperationResultsAsync())
		{
			items.Add(result);
		}

		items.Count.Should().Be(2);

		for (var i = 0; i < items.Count; i++)
		{
			items[i].OperationIndex.Should().Be(i);
		}
	}

	[Fact]
	public async Task Errors_YieldsEarlyWhenNoErrors()
	{
		using var stream = new TrackDisposeStream(ErrorsAndStatusNoErrors);
		using var response = await StreamingBulkResponse.CreateAsync(stream, ResponseMode.Errors);

		response.Errors.Should().BeFalse();
		response.Took.Should().Be(20757446561);

		var items = new List<OperationResult>();

		await foreach (var result in response.GetOperationResultsAsync())
		{
			items.Add(result);
		}

		items.Count.Should().Be(0);
		stream.IsDisposed.Should().BeTrue();
	}

	private class TrackDisposeStream : MemoryStream
	{
		public TrackDisposeStream() { }
		public TrackDisposeStream(byte[] bytes) : base(bytes) { }
		public TrackDisposeStream(byte[] bytes, int index, int count) : base(bytes, index, count) { }

		public bool IsDisposed { get; private set; }

		protected override void Dispose(bool disposing)
		{
			IsDisposed = true;
			base.Dispose(disposing);
		}
	}
}
#endif
