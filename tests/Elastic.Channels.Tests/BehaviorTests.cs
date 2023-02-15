// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels.Diagnostics;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Elastic.Channels.Tests
{
	public class BehaviorTests : IDisposable
	{
		public BehaviorTests(ITestOutputHelper testOutput) => XunitContext.Register(testOutput);

		void IDisposable.Dispose() => XunitContext.Flush();

		[Fact] public async Task RespectsPagination()
		{
			int totalEvents = 500_000, maxInFlight = totalEvents / 5, bufferSize = maxInFlight / 10;
			var expectedSentBuffers = totalEvents / bufferSize;
			var bufferOptions = new BufferOptions
			{
				WaitHandle = new CountdownEvent(expectedSentBuffers), MaxInFlightMessages = maxInFlight, MaxConsumerBufferSize = bufferSize,
			};
			var channel = new NoopBufferedChannel(bufferOptions);

			var written = 0;
			for (var i = 0; i < totalEvents; i++)
			{
				var e = new NoopBufferedChannel.NoopEvent();
				if (await channel.WaitToWriteAsync(e))
					written++;
			}
			var signalled = bufferOptions.WaitHandle.Wait(TimeSpan.FromSeconds(5));
			signalled.Should().BeTrue("The channel was not drained in the expected time");
			written.Should().Be(totalEvents);
			channel.SentBuffersCount.Should().Be(expectedSentBuffers);
		}

		/// <summary>
		/// If we are feeding data slowly e.g smaller than <see cref="BufferOptions.MaxConsumerBufferSize"/>
		/// we don't want this data equally distributed over multiple calls to export the data.
		/// Instead we want the smaller buffer to go out over a single export to the external system
		/// </summary>
		[Fact] public async Task MessagesAreSequentiallyDistributedOverWorkers()
		{
			int totalEvents = 500_000, maxInFlight = totalEvents / 5, bufferSize = maxInFlight / 10;
			var bufferOptions = new BufferOptions
			{
				WaitHandle = new CountdownEvent(1),
				MaxInFlightMessages = maxInFlight,
				MaxConsumerBufferSize = bufferSize,
				MaxConsumerBufferLifetime = TimeSpan.FromMilliseconds(500)
			};

			var channel = new NoopBufferedChannel(bufferOptions);
			var written = 0;
			for (var i = 0; i < 100; i++)
			{
				var e = new NoopBufferedChannel.NoopEvent();
				if (await channel.WaitToWriteAsync(e))
					written++;
			}
			var signalled = bufferOptions.WaitHandle.Wait(TimeSpan.FromSeconds(1));
			signalled.Should().BeTrue("The channel was not drained in the expected time");
			written.Should().Be(100);
			channel.SentBuffersCount.Should().Be(1);
		}

		[Fact] public async Task ConcurrencyIsApplied()
		{
			int totalEvents = 5_000, maxInFlight = 5_000, bufferSize = 500;
			var expectedPages = totalEvents / bufferSize;
			var bufferOptions = new BufferOptions
			{
				WaitHandle = new CountdownEvent(expectedPages),
				MaxInFlightMessages = maxInFlight,
				MaxConsumerBufferSize = bufferSize,
				ConcurrentConsumers = 4
			};

			var channel = new NoopBufferedChannel(bufferOptions, observeConcurrency: true);

			var written = 0;
			for (var i = 0; i < totalEvents; i++)
			{
				var e = new NoopBufferedChannel.NoopEvent();
				if (await channel.WaitToWriteAsync(e))
					written++;
			}
			var signalled = bufferOptions.WaitHandle.Wait(TimeSpan.FromSeconds(5));
			signalled.Should().BeTrue("The channel was not drained in the expected time");
			written.Should().Be(totalEvents);
			channel.SentBuffersCount.Should().Be(expectedPages);
			channel.ObservedConcurrency.Should().Be(4);
		}

		[Fact] public void ManyChannelsContinueToDoWork()
		{
			int totalEvents = 50_000_000, maxInFlight = totalEvents / 5, bufferSize = maxInFlight / 10;
			for (var c = 0; c < 10; c++)
			{
				var expectedSentBuffers = totalEvents / bufferSize;
				var bufferOptions = new BufferOptions
				{
					WaitHandle = new CountdownEvent(expectedSentBuffers),
					MaxInFlightMessages = maxInFlight,
					MaxConsumerBufferSize = bufferSize,
					MaxConsumerBufferLifetime = TimeSpan.FromMilliseconds(20)
				};
				using var channel = new NoopBufferedChannel(bufferOptions);
				var written = 0;
				var _ = Task.Factory.StartNew(async () =>
				{
					for (var i = 0; i < totalEvents && !channel.Options.BufferOptions.WaitHandle.IsSet; i++)
					{
						var e = new NoopBufferedChannel.NoopEvent();
						if (await channel.WaitToWriteAsync(e))
							written++;
					}
				});
				// wait for some work to have progressed
				bufferOptions.WaitHandle.Wait(TimeSpan.FromMilliseconds(50));
				channel.SentBuffersCount.Should().BeGreaterThan(0);
				written.Should().BeLessThan(totalEvents);
			}
		}
	}
}
