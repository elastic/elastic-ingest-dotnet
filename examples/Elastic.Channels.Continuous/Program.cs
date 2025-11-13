// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading.Channels;
using Elastic.Channels;
using Elastic.Channels.Diagnostics;

var ctxs = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) => {
	ctxs.Cancel();
	eventArgs.Cancel = true;
};

var options = new NoopBufferedChannel.NoopChannelOptions
{
	//TrackConcurrency = true,
	BufferOptions = new BufferOptions
	{
		OutboundBufferMaxLifetime = TimeSpan.Zero,
		InboundBufferMaxSize = 1_000_000,
		OutboundBufferMaxSize = 1_000_000
	},
	ExportBufferCallback = () => Console.Write("."),
	ExportExceptionCallback = e => Console.Write("!")

};
Console.WriteLine("2");
var channel = new DiagnosticsBufferedChannel(options);
Console.WriteLine($"Begin: ({channel.OutboundStarted}) {channel.MaxConcurrency} {channel.InflightExportOperations} -> {channel.InflightEvents}");
await Parallel.ForEachAsync(Enumerable.Range(0, int.MaxValue), new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ctxs.Token }, async (i, ctx) =>
{
	var e = new NoopBufferedChannel.NoopEvent { Id = i };
	if (await channel.WaitToWriteAsync(e, ctx))
	{

	}

	if (i % 10_000 == 0)
	{
		Console.Clear();
		Console.WriteLine(channel);
	}
});
