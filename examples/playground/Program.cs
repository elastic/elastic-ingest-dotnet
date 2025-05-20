// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using System.Threading.Channels;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;

var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
	cancellationTokenSource.Cancel();
	eventArgs.Cancel = true;
};

const int numDocs = 1_000_000;
var bufferOptions = new BufferOptions
{
	InboundBufferMaxSize = 1_000_000,
	OutboundBufferMaxSize = 5_000,
	ExportMaxConcurrency = Environment.ProcessorCount,
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


async Task PushToChannel(DataStreamChannel<EcsDocument> c)
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

	async Task DoChannelWrite(int i, CancellationToken cancellationToken)
	{
		var message = $"Logging information {i} - Random value: {random.NextDouble()}";
		var doc = new EcsDocument { Timestamp = DateTimeOffset.UtcNow, Message = message };
		if (await c.WaitToWriteAsync(cancellationToken) && c.TryWrite(doc))
			return;

		Console.WriteLine("Failed To write");
	}
}

DataStreamChannel<EcsDocument> SetupElasticsearchChannel()
{
	var apiKey = Environment.GetEnvironmentVariable("ELASTIC_API_KEY") ?? throw new Exception();
	var url = Environment.GetEnvironmentVariable("ELASTIC_URL") ?? throw new Exception();

	var configuration = new ElasticsearchConfiguration(new Uri(url), new ApiKey(apiKey));
	var transport = new DistributedTransport(configuration);
	var c = new DataStreamChannel<EcsDocument>(
		new DataStreamChannelOptions<EcsDocument>(transport)
		{
			BufferOptions = bufferOptions,
			CancellationToken = cancellationTokenSource.Token,
			SerializerContext = ExampleJsonSerializerContext.Default,
			ExportResponseCallback = (c, t) =>
			{
				Console.WriteLine($"{c.ApiCallDetails.HttpMethod} Response: {c.ApiCallDetails.HttpStatusCode}");
			}
		});

	return c;
}

public class EcsDocument
{
	[JsonPropertyName("@timestamp")]
	public DateTimeOffset Timestamp { init; get; }

	[JsonPropertyName("message")]
	public string Message { init; get; } = null!;
}

[JsonSerializable(typeof(EcsDocument))]
internal partial class ExampleJsonSerializerContext : JsonSerializerContext;

