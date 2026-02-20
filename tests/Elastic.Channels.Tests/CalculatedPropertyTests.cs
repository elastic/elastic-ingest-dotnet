// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using Elastic.Channels.Diagnostics;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Channels.Tests;

public class CalculatedPropertyTests
{
	[Test]
	[Arguments(500_000, 50_000, 100_000)]
	[Arguments(10_00_000, 50_000, 100_000)]
	[Arguments(50_000, 50_000, 25_000)]
	[Arguments(10_000, 50_000, 20_000)]
	[Arguments(10_00_000, 1_000, 2_000)]
	public void BatchExportSizeAndDrainSizeConstraints(int maxInFlight, int bufferSize, int drainSize)
	{
		var bufferOptions = new BufferOptions
		{
			InboundBufferMaxSize = maxInFlight,
			OutboundBufferMaxSize = bufferSize,
		};
		var channel = new NoopBufferedChannel(bufferOptions);

		var expectedConcurrency =
			Math.Max(1, Math.Min(maxInFlight / bufferSize, Environment.ProcessorCount * 2));
		channel.MaxConcurrency.Should().Be(expectedConcurrency);
		if (maxInFlight >= bufferSize)
			channel.BatchExportSize.Should().Be(bufferSize);
		else
			channel.BatchExportSize.Should().Be(maxInFlight / expectedConcurrency);

		// drain size is maxed out at 100_000
		channel.DrainSize.Should().Be(Math.Min(100_000, drainSize));

	}

}
