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

namespace Elastic.Channels.Tests;

public class TroubleshootTests : IDisposable
{
	public TroubleshootTests(ITestOutputHelper testOutput) => XunitContext.Register(testOutput);
	void IDisposable.Dispose() => XunitContext.Flush();

	[Fact] public async Task CanDisableDiagnostics()
	{
		var (totalEvents, expectedSentBuffers, bufferOptions) = Setup();
		var channel = new NoopBufferedChannel(new NoopBufferedChannel.NoopChannelOptions()
		{
			DisableDiagnostics = true,
			BufferOptions = bufferOptions
		});

		await WriteExpectedEvents(totalEvents, channel, bufferOptions, expectedSentBuffers);

		channel.ToString().Should().Contain("Diagnostics.NoopBufferedChannel");
		channel.ToString().Should().NotContain("Successful publish over channel: NoopBufferedChannel.");
		channel.ToString().Should().NotContain($"Exported Buffers: {expectedSentBuffers:N0}");
	}

	[Fact] public async Task DefaultIncludesDiagnostics()
	{
		var (totalEvents, expectedSentBuffers, bufferOptions) = Setup();
		var channel = new NoopBufferedChannel(new NoopBufferedChannel.NoopChannelOptions()
		{
			BufferOptions = bufferOptions
		});

		await WriteExpectedEvents(totalEvents, channel, bufferOptions, expectedSentBuffers);

		channel.ToString().Should().NotContain("Diagnostics.NoopBufferedChannel");
		channel.ToString().Should().Contain("Successful publish over channel: NoopBufferedChannel.");
		channel.ToString().Should().Contain($"Exported Buffers:");
	}

	[Fact] public async Task DiagnosticsChannelAlwaysIncludesDiagnosticsInToString()
	{
		var (totalEvents, expectedSentBuffers, bufferOptions) = Setup();
		var channel = new DiagnosticsBufferedChannel(bufferOptions);

		await WriteExpectedEvents(totalEvents, channel, bufferOptions, expectedSentBuffers);

		channel.ToString().Should().NotContain("Diagnostics.DiagnosticsBufferedChannel");
		channel.ToString().Should().Contain("Successful publish over channel: DiagnosticsBufferedChannel.");
		channel.ToString().Should().Contain($"Exported Buffers: {expectedSentBuffers:N0}");
	}

	private static async Task WriteExpectedEvents(int totalEvents, NoopBufferedChannel channel, BufferOptions bufferOptions, int expectedSentBuffers)
	{
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

	private static (int totalEvents, int expectedSentBuffers, BufferOptions bufferOptions) Setup()
	{
		int totalEvents = 5000, maxInFlight = totalEvents / 5, bufferSize = maxInFlight / 10;
		var expectedSentBuffers = totalEvents / bufferSize;
		var bufferOptions = new BufferOptions
		{
			WaitHandle = new CountdownEvent(expectedSentBuffers),
			InboundBufferMaxSize = maxInFlight,
			OutboundBufferMaxSize = bufferSize,
		};
		return (totalEvents, expectedSentBuffers, bufferOptions);
	}

}
