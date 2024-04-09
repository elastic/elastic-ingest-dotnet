// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Channels;
using Elastic.Channels.Diagnostics;

var ctxs = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) => {
	ctxs.Cancel();
	eventArgs.Cancel = true;
};

var options = new NoopBufferedChannel.NoopChannelOptions
{
	BufferOptions = new BufferOptions()
	{
		OutboundBufferMaxSize = 10_000,
		InboundBufferMaxSize = 10_000_000,
	},
	ExportBufferCallback = () => Console.Write("."),
	ExportExceptionCallback = e => Console.Write("!"),
	PublishToInboundChannelFailureCallback  = () => Console.Write("I"),
	PublishToOutboundChannelFailureCallback  = () => Console.Write("O"),

};
var channel = new DiagnosticsBufferedChannel(options);
await Parallel.ForEachAsync(Enumerable.Range(0, int.MaxValue), new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ctxs.Token }, async (i, ctx) =>
{
	var e = new NoopBufferedChannel.NoopEvent { Id = i };
	var written = false;
	//Console.Write('.');
	var ready = await channel.WaitToWriteAsync(ctx);
	if (ready) written = channel.TryWrite(e);
	if (!written)
	{
		Console.WriteLine();
		Console.WriteLine(channel);
		Console.WriteLine(i);
		Environment.Exit(1);
	}
});
