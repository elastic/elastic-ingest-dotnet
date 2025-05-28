// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using System.Threading.Channels;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.Semantic;
using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;

var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
	cancellationTokenSource.Cancel();
	eventArgs.Cancel = true;
};

const int numDocs = 1_000;
var bufferOptions = new BufferOptions
{
	InboundBufferMaxSize = 1_000_000,
	OutboundBufferMaxSize = 10,
	BoundedChannelFullMode = BoundedChannelFullMode.Wait
};

var memoryBefore = GC.GetTotalMemory(false);

await DoWork();

// This is not really indicative because the channel is still draining at this point in time
var memoryAfter = GC.GetTotalMemory(false);
Console.WriteLine($"Memory before: {memoryBefore} bytes");
Console.WriteLine($"Memory after: {memoryAfter} bytes");
var memoryUsed = memoryAfter - memoryBefore;
Console.WriteLine($"Memory used: {memoryUsed} bytes");
GC.Collect();
var memoryAfterCollect = GC.GetTotalMemory(false);
Console.WriteLine($"Memory used after collect: {memoryAfterCollect} bytes");

async Task DoWork()
{
	using var channel = SetupElasticsearchChannel();

	await PushToChannel(channel);
}


async Task PushToChannel(SemanticIndexChannel<MyDocument> c)
{
	var random = new Random();
	if (c == null) throw new ArgumentNullException(nameof(c));

	await c.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

	foreach (var i in Enumerable.Range(0, numDocs))
		await DoChannelWrite(i, cancellationTokenSource.Token);

	Console.WriteLine("---> Wrote all documents");

	var drained = await c.WaitForDrainAsync();
	Console.WriteLine(!drained
		? $"---> Failed to drain channel {c.InflightExportOperations} pending buffers."
		: $"---> Drained channel {c.InflightEvents} pending buffers.");
	await c.RefreshAsync();

	await c.ApplyAliasesAsync();
	await c.ApplyLatestAliasAsync();
	await c.ApplyActiveSearchAliasAsync();

	async Task DoChannelWrite(int i, CancellationToken cancellationToken)
	{
		var message = $"Logging information {i} - Random value: {random.NextDouble()}";
		var doc = new MyDocument { Message = message };
		if (await c.WaitToWriteAsync(cancellationToken) && c.TryWrite(doc))
			return;

		Console.WriteLine("Failed To write");
	}
}

SemanticIndexChannel<MyDocument> SetupElasticsearchChannel()
{
	var apiKey = Environment.GetEnvironmentVariable("ELASTIC_API_KEY") ?? throw new Exception();
	var url = Environment.GetEnvironmentVariable("ELASTIC_URL") ?? throw new Exception();

	var configuration = new ElasticsearchConfiguration(new Uri(url), new ApiKey(apiKey))
	{
		ProxyAddress = "http://localhost:8866"
	};
	var transport = new DistributedTransport(configuration);
	var c = new SemanticIndexChannel<MyDocument>(
		new SemanticIndexChannelOptions<MyDocument>(transport)
		{
			BufferOptions = bufferOptions,
			CancellationToken = cancellationTokenSource.Token,
			SerializerContext = ExampleJsonSerializerContext.Default,
			ExportResponseCallback = (c, _) =>
			{
				Console.WriteLine($"{c.ApiCallDetails.HttpMethod} Response: {c.ApiCallDetails.HttpStatusCode}");
			},
			InferenceCreateTimeout = TimeSpan.FromMinutes(5),
			// language=json
			GetMapping = (inferenceId, searchInferenceId) =>
				$$"""
				{
				  "properties": {
				    "message": {
				        "type": "text",
				        "fields": {
				            "semantic": {
				                "type": "semantic_text",
				                "inference_id": "{{inferenceId}}"
				            }
				        }
				    }
				  }
				}
				"""
		});

	return c;
}

public class MyDocument
{
	[JsonPropertyName("message")]
	public string Message { init; get; } = null!;
}

[JsonSerializable(typeof(MyDocument))]
internal partial class ExampleJsonSerializerContext : JsonSerializerContext;

