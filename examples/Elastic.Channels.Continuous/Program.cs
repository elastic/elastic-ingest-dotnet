// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Channels;
using Elastic.Channels.Diagnostics;

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
for (long i = 0; i < long.MaxValue; i++)
{
	var e = new NoopBufferedChannel.NoopEvent { Id = i };
	var written = false;
	var ready = await channel.WaitToWriteAsync();
	if (ready) written = channel.TryWrite(e);
	if (!written || channel.BufferMismatches > 0)
	{
		Console.WriteLine();
		Console.WriteLine(channel);
		Console.WriteLine(i);
		Environment.Exit(1);
	}

}

