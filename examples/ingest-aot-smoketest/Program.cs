// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Transport;

var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
	cancellationTokenSource.Cancel();
	eventArgs.Cancel = true;
};

const int numDocs = 1_000;

var invoker = new InMemoryRequestInvoker();
var pool = new StaticNodePool([new Node(new Uri("http://localhost:9200"))]);
var configuration = new TransportConfiguration(pool, invoker);
var transport = new DistributedTransport(configuration);
var options = new DataStreamChannelOptions<MyDocument>(transport)
{
	SerializerContext = ExampleJsonSerializerContext.Default,
	ExportBufferCallback = () => Console.Write('.'),
	ExportMaxRetriesCallback = _ => Console.Write('!'),
	ExportExceptionCallback = e => Console.WriteLine(e),
	BufferOptions = { ExportMaxConcurrency = 1, OutboundBufferMaxSize = numDocs / 10 }
};

var channel = new DataStreamChannel<MyDocument>(options);
await PushToChannel(channel);
await channel.WaitForDrainAsync();
Console.WriteLine();
Console.WriteLine(channel);


async Task PushToChannel(DataStreamChannel<MyDocument> c)
{
	var random = new Random();
	if (c == null) throw new ArgumentNullException(nameof(c));

	foreach (var i in Enumerable.Range(0, numDocs))
		await DoChannelWrite(i, cancellationTokenSource.Token);

	async Task DoChannelWrite(int i, CancellationToken cancellationToken)
	{
		var message = $"Logging information {i} - Random value: {random.NextDouble()}";
		var doc = new MyDocument { Message = message };
		if (await c.WaitToWriteAsync(cancellationToken) && c.TryWrite(doc))
			return;

		Console.WriteLine("Failed To write");
	}
}




public class MyDocument
{
	[JsonPropertyName("@timestamp")]
	public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

	[JsonPropertyName("message")]
	public string Message { get; init; } = null!;
}

[JsonSerializable(typeof(MyDocument))]
internal partial class ExampleJsonSerializerContext : JsonSerializerContext;
