// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Channels;
using Elastic.Elasticsearch.Ephemeral;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Transport;

var random = new Random();
var ctxs = new CancellationTokenSource();
var parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ctxs.Token };
const int numDocs = 1_000_000;
var bufferOptions = new BufferOptions { InboundBufferMaxSize = numDocs, OutboundBufferMaxSize = 10_000 };
var config = new EphemeralClusterConfiguration("8.13.0");
using var cluster = new EphemeralCluster(config);
using var channel = SetupElasticsearchChannel();

Console.CancelKeyPress += (sender, eventArgs) =>
{
	ctxs.Cancel();
	cluster.Dispose();
	eventArgs.Cancel = true;
};


using var started = cluster.Start();

var memoryBefore = GC.GetTotalMemory(false);

await PushToChannel(channel);

// This is not really indicative because the channel is still draining at this point in time
var memoryAfter = GC.GetTotalMemory(false);
Console.WriteLine($"Memory before: {memoryBefore} bytes");
Console.WriteLine($"Memory after: {memoryAfter} bytes");
var memoryUsed = memoryAfter - memoryBefore;
Console.WriteLine($"Memory used: {memoryUsed} bytes");

Console.WriteLine($"Press any key...");
Console.ReadKey();


async Task PushToChannel(DataStreamChannel<EcsDocument> c)
{
	if (c == null) throw new ArgumentNullException(nameof(c));

	await c.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
	await Parallel.ForEachAsync(Enumerable.Range(0, numDocs), parallelOpts, async (i, ctx) =>
	{
		await DoChannelWrite(i, ctx);
	});

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
	var transportConfiguration = new TransportConfiguration(new Uri("http://localhost:9200"));
	var c = new DataStreamChannel<EcsDocument>(
		new DataStreamChannelOptions<EcsDocument>(new DistributedTransport(transportConfiguration))
		{
			BufferOptions = bufferOptions,
			CancellationToken = ctxs.Token
			, ExportResponseCallback = (c, t) =>
			{
				var error = c.Items.Select(i=>i.Error).FirstOrDefault(e => e != null);
				if (error == null) return;
				Console.WriteLine(error.ToString());
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

