// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Linq;
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
				WaitHandle = new CountdownEvent(expectedSentBuffers), InboundBufferMaxSize = maxInFlight, OutboundBufferMaxSize = bufferSize,
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
			channel.ExportedBuffers.Should().Be(expectedSentBuffers);
		}

		/// <summary>
		/// If we are feeding data slowly e.g smaller than <see cref="BufferOptions.OutboundBufferMaxSize"/>
		/// we don't want this data equally distributed over multiple calls to export the data.
		/// Instead we want the smaller buffer to go out over a single export to the external system
		/// </summary>
		[Fact] public async Task MessagesAreSequentiallyDistributedOverWorkers()
		{
			int totalEvents = 500_000, maxInFlight = totalEvents / 5, bufferSize = maxInFlight / 10;
			var bufferOptions = new BufferOptions
			{
				WaitHandle = new CountdownEvent(1),
				InboundBufferMaxSize = maxInFlight,
				OutboundBufferMaxSize = bufferSize,
				OutboundBufferMaxLifetime = TimeSpan.FromMilliseconds(500)
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
			channel.ExportedBuffers.Should().Be(1);
		}

		[Fact] public async Task ConcurrencyIsApplied()
		{
			int totalEvents = 5_000, maxInFlight = 5_000, bufferSize = 500;
			var expectedPages = totalEvents / bufferSize;
			var bufferOptions = new BufferOptions
			{
				WaitHandle = new CountdownEvent(expectedPages),
				InboundBufferMaxSize = maxInFlight,
				OutboundBufferMaxSize = bufferSize,
				ExportMaxConcurrency = 4
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
			channel.ExportedBuffers.Should().Be(expectedPages);
			channel.ObservedConcurrency.Should().BeGreaterThan(1);
		}

		[Fact] public async Task ManyChannelsContinueToDoWork()
		{
			int totalEvents = 50_000_000, maxInFlight = totalEvents / 5, bufferSize = maxInFlight / 10;
			int closedThread = 0, maxFor = Environment.ProcessorCount * 2;
			var expectedSentBuffers = totalEvents / bufferSize;

			Task StartChannel(int taskNumber)
			{
				var bufferOptions = new BufferOptions
				{
					WaitHandle = new CountdownEvent(expectedSentBuffers),
					InboundBufferMaxSize = maxInFlight,
					OutboundBufferMaxSize = 1000,
					OutboundBufferMaxLifetime = TimeSpan.FromMilliseconds(20)
				};
				using var channel = new DiagnosticsBufferedChannel(bufferOptions, name: $"Task {taskNumber}");
				var written = 0;
				var t = Task.Factory.StartNew(async () =>
				{
					for (var i = 0; i < totalEvents; i++)
					{
						var e = new NoopBufferedChannel.NoopEvent();
						if (await channel.WaitToWriteAsync(e))
							written++;
					}
				}, TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness);
				// wait for some work to have progressed
				bufferOptions.WaitHandle.Wait(TimeSpan.FromMilliseconds(500));

				written.Should().BeGreaterThan(0).And.BeLessThan(totalEvents);
				channel.ExportedBuffers.Should().BeGreaterThan(0, "Parallel invocation: {0} channel: {1}", taskNumber, channel);
				Interlocked.Increment(ref closedThread);
				return t;
			}

			var tasks = Enumerable.Range(0, maxFor).Select(i => Task.Factory.StartNew(() => StartChannel(i), TaskCreationOptions.LongRunning | TaskCreationOptions.PreferFairness)).ToArray();

			await Task.WhenAll(tasks);

			closedThread.Should().BeGreaterThan(0).And.Be(maxFor);
		}

		[Fact] public async Task SlowlyPushEvents()
		{
			int totalEvents = 50_000_000, maxInFlight = totalEvents / 5, bufferSize = maxInFlight / 10;
			var expectedSentBuffers = totalEvents / bufferSize;
			var bufferOptions = new BufferOptions
			{
				WaitHandle = new CountdownEvent(expectedSentBuffers),
				InboundBufferMaxSize = maxInFlight,
				OutboundBufferMaxSize = 10_000,
				OutboundBufferMaxLifetime = TimeSpan.FromMilliseconds(100)
			};
			using var channel = new DiagnosticsBufferedChannel(bufferOptions, name: $"Slow push channel");
			await Task.Delay(TimeSpan.FromMilliseconds(200));
			var written = 0;
			var _ = Task.Factory.StartNew(async () =>
			{
				for (var i = 0; i < totalEvents && !channel.Options.BufferOptions.WaitHandle.IsSet; i++)
				{
					var e = new NoopBufferedChannel.NoopEvent();
					if (await channel.WaitToWriteAsync(e).ConfigureAwait(false))
						written++;
					await Task.Delay(TimeSpan.FromMilliseconds(40)).ConfigureAwait(false);
				}
			}, TaskCreationOptions.LongRunning);
			// wait for some work to have progressed
			bufferOptions.WaitHandle.Wait(TimeSpan.FromMilliseconds(500));
			//Ensure we written to the channel but not enough to satisfy OutboundBufferMaxSize
			written.Should().BeGreaterThan(0).And.BeLessThan(10_000);
			//even though OutboundBufferMaxSize was not hit we should still observe an invocation to Export()
			//because OutboundBufferMaxLifetime was hit
			channel.ExportedBuffers.Should().BeGreaterThan(0, "{0}", channel);
		}
	}
}
