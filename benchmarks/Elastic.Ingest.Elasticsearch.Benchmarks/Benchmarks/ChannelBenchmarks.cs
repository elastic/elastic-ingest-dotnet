// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Channels;
using Elastic.Channels.Diagnostics;

namespace Elastic.Ingest.Elasticsearch.Benchmarks;

/// <summary>
/// The buffered channel on its own, with an export that does nothing: what moving events from the inbound channel through the
/// buffers to the exports costs, apart from serializing them and talking to Elasticsearch.
/// </summary>
[MemoryDiagnoser]
public class ChannelBenchmarks
{
	private NoopBufferedChannel.NoopEvent[] _events = [];

	public int Events { get; set; } = 100_000;

	public int BatchSize { get; set; } = 1_000;

	[GlobalSetup]
	public void Setup() => _events = Enumerable.Range(0, Events).Select(i => new NoopBufferedChannel.NoopEvent { Id = i }).ToArray();

	// diagnostics off, as a service that cares about throughput runs: the default listener allocates for every event
	private NoopBufferedChannel CreateChannel(int concurrency) =>
		new(new NoopBufferedChannel.NoopChannelOptions
		{
			DisableDiagnostics = true,
			BufferOptions = new BufferOptions
			{
				InboundBufferMaxSize = Events,
				OutboundBufferMaxSize = BatchSize,
				ExportMaxConcurrency = concurrency,
				OutboundBufferMaxLifetime = TimeSpan.FromHours(1)
			}
		});

	[Benchmark(Baseline = true)]
	public async Task<bool> OneWorker()
	{
		using var channel = CreateChannel(1);
		channel.TryWriteMany(_events);
		return await channel.WaitForDrainAsync(TimeSpan.FromMinutes(1));
	}

	[Benchmark]
	public async Task<bool> FourWorkers()
	{
		using var channel = CreateChannel(4);
		channel.TryWriteMany(_events);
		return await channel.WaitForDrainAsync(TimeSpan.FromMinutes(1));
	}

	/// <summary>Writers that wait for room, as a producer faster than the exports does.</summary>
	[Benchmark]
	public async Task<bool> BackPressure()
	{
		using var channel = new NoopBufferedChannel(new NoopBufferedChannel.NoopChannelOptions
		{
			DisableDiagnostics = true,
			BufferOptions = new BufferOptions
			{
				InboundBufferMaxSize = 2_000,
				OutboundBufferMaxSize = BatchSize,
				ExportMaxConcurrency = 2,
				OutboundBufferMaxLifetime = TimeSpan.FromHours(1)
			}
		});
		foreach (var e in _events)
			await channel.WaitToWriteAsync(e);
		return await channel.WaitForDrainAsync(TimeSpan.FromMinutes(1));
	}
}
