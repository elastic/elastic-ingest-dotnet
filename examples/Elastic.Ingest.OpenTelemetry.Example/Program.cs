// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Ingest.OpenTelemetry;


if (args.Length != 2)
{
	Console.Error.WriteLine("Program needs two arguments: <url> <secret_token>");
	Environment.Exit(1);
}

var channel = new TraceChannel(new TraceChannelOptions
{
	ServiceName = "hello-world",
	Endpoint = new Uri(args[0]),
	SecretToken = args[1],
	BufferOptions = new BufferOptions
	{

	}
});

var random = new Random();
long numTraces = 0;
while (true)
{
	var outerActivity = new Activity("outer");
	outerActivity.Start();
	await Task.Delay(TimeSpan.FromMilliseconds(random.Next(20, 200))).ConfigureAwait(false);

	for (var i = 0; i < random.Next(1, 5); i++)
	{
		var innerActivity = new Activity($"inner {i}");
		innerActivity.Start();
		await Task.Delay(TimeSpan.FromMilliseconds(random.Next(5, 50))).ConfigureAwait(false);
		innerActivity.SetStatus(ActivityStatusCode.Ok);
		innerActivity.Stop();
		channel.TryWrite(innerActivity);
	}
	outerActivity.SetStatus(ActivityStatusCode.Ok);
	outerActivity.Stop();
	channel.TryWrite(outerActivity);
	Interlocked.Increment(ref numTraces);
	Console.Write($"\r Queued {numTraces} traces");
}
