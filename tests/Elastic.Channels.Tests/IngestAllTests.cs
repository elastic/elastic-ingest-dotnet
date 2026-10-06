// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Elastic.Channels.Diagnostics;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Channels.Tests;

/// <summary>Pull mode on the generic channel, independent of any Elasticsearch specifics.</summary>
public class IngestAllTests
{
	private static IEnumerable<NoopBufferedChannel.NoopEvent> Events(int n) =>
		Enumerable.Range(0, n).Select(i => new NoopBufferedChannel.NoopEvent { Id = i });

	private static async IAsyncEnumerable<NoopBufferedChannel.NoopEvent> AsyncEvents(int n)
	{
		foreach (var e in Events(n))
		{
			await Task.Yield();
			yield return e;
		}
	}

	[Test]
	public async Task ExportsEveryBatchAndNothingIsLeftInFlight()
	{
		using var channel = new NoopBufferedChannel(new BufferOptions { OutboundBufferMaxSize = 10, ExportMaxConcurrency = 2 });
		var result = await channel.IngestAllAsync(Events(95));

		result.Read.Should().Be(95);
		result.Batches.Should().Be(10);
		channel.ExportedBuffers.Should().Be(10);
		channel.InflightExportOperations.Should().Be(0);
		channel.InflightEvents.Should().Be(0);
	}

	[Test]
	public async Task EmptySourcesCompleteImmediately()
	{
		using var channel = new NoopBufferedChannel(new BufferOptions { OutboundBufferMaxSize = 10 });
		(await channel.IngestAllAsync(Events(0))).Batches.Should().Be(0);
		(await channel.IngestAllAsync(AsyncEvents(0))).Batches.Should().Be(0);
		channel.ExportedBuffers.Should().Be(0);
	}

	[Test]
	public async Task ObservedConcurrencyNeverExceedsTheRequestedLimit()
	{
		using var channel = new NoopBufferedChannel(new BufferOptions { OutboundBufferMaxSize = 5, ExportMaxConcurrency = 8 }, observeConcurrency: true);
		await channel.IngestAllAsync(Events(60), maxConcurrency: 2);

		channel.ObservedConcurrency.Should().BeInRange(1, 2);
	}

	[Test]
	public async Task ASourceThatThrowsSurfacesTheErrorAndSettlesInflightWork()
	{
		using var channel = new NoopBufferedChannel(new BufferOptions { OutboundBufferMaxSize = 5, ExportMaxConcurrency = 2 });
		IEnumerable<NoopBufferedChannel.NoopEvent> Broken()
		{
			foreach (var e in Events(23)) yield return e;
			throw new InvalidOperationException("source failed");
		}

		var act = () => channel.IngestAllAsync(Broken());
		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("source failed");
		channel.InflightExportOperations.Should().Be(0);
	}

	[Test]
	public async Task BatchesAndReadMatchTheModelForRandomShapes()
	{
		await Gen.Select(Gen.Int[0, 300], Gen.Int[1, 40], Gen.Int[1, 6], Gen.Int[1, 6], Gen.Bool).SampleAsync(async x =>
		{
			var (n, batch, channelConcurrency, pullConcurrency, useAsync) = x;
			using var channel = new NoopBufferedChannel(new BufferOptions { OutboundBufferMaxSize = batch, ExportMaxConcurrency = channelConcurrency });

			var result = useAsync
				? await channel.IngestAllAsync(AsyncEvents(n), pullConcurrency)
				: await channel.IngestAllAsync(Events(n), pullConcurrency);

			var size = channel.BatchExportSize;
			result.Read.Should().Be(n);
			result.Batches.Should().Be((n + size - 1) / size);
			channel.ExportedBuffers.Should().Be(result.Batches);
			channel.InflightExportOperations.Should().Be(0);
		}, iter: 150);
	}
}
