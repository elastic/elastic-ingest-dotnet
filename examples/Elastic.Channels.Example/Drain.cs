// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading.Channels;

namespace Elastic.Channels.Example;

public static class Drain
{
	public static async Task<(int, int)> RegularChannel(int totalEvents, int maxInFlight)
	{
		int written = 0, read = 0;
		var options = new BoundedChannelOptions(maxInFlight);
		var inChannel = Channel.CreateBounded<NoopBufferedChannel.NoopEvent>(options);
		var x = new CountdownEvent(2);
		var thread = new Thread(async o =>
		{
			while (await inChannel.Reader.WaitToReadAsync())
			while (inChannel.Reader.TryRead(out var item))
				read++;

			x.Signal();
		});
		thread.Start();

		for (var i = 0; i < totalEvents; i++)
		{
			if (!await inChannel.Writer.WaitToWriteAsync()) continue;

			var e = new NoopBufferedChannel.NoopEvent();
			if (inChannel.Writer.TryWrite(e))
				written++;
		}
		inChannel.Writer.TryComplete();
		x.Signal();
		x.Wait(TimeSpan.FromSeconds(60));
		return (written, read);
	}

	public static async Task<(int, NoopBufferedChannel)> ElasticChannel(
		int totalEvents, int maxInFlight, int bufferSize, int concurrency, int expectedSentBuffers)
	{
		var written = 0;

		var bufferOptions = new BufferOptions
		{
			WaitHandle = new CountdownEvent(expectedSentBuffers),
			MaxInFlightMessages = maxInFlight,
			MaxConsumerBufferSize = bufferSize,
			ConcurrentConsumers = concurrency
		};
		var channel = new NoopBufferedChannel(bufferOptions);

		for (var i = 0; i < totalEvents; i++)
		{
			var e = new NoopBufferedChannel.NoopEvent();
			if (await channel.WaitToWriteAsync(e))
				written++;
		}

		bufferOptions.WaitHandle.Wait(TimeSpan.FromSeconds(60));
		return (written, channel);

	}
}
