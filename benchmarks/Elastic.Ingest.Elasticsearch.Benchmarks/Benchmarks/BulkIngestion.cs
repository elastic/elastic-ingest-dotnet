// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using BenchmarkDotNet.Engines;
using Performance.Common;

namespace Elastic.Ingest.Elasticsearch.Benchmarks;

[SimpleJob(RunStrategy.Monitoring, invocationCount: 10, id: "BulkIngestionJob")]
public class BulkIngestion
{
	private static readonly int MaxExportSize = 5_000;
	
	private readonly ManualResetEvent _waitHandle = new(false);
	private StockData[] _data = Array.Empty<StockData>();
	private IndexChannelOptions<StockData>? _options;

#if DEBUG
	private long _responses;
#endif

	//[Params(100_000)]
	public int DocumentsToIndex { get; set; } = 100_000;

	[ParamsAllValues]
	public bool DisableDiagnostics { get; set; }

	[ParamsAllValues]
	public bool UseReadOnlyMemory { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		_data = StockData.CreateSampleData(DocumentsToIndex);

		var transport = new DefaultHttpTransport(
			new TransportConfiguration(
				new SingleNodePool(new("http://localhost:9200")),
				new InMemoryConnection(StockData.CreateSampleDataSuccessWithFilterPathResponseBytes(MaxExportSize))));

#pragma warning disable CS0618 // Type or member is obsolete
		_options = new IndexChannelOptions<StockData>(transport)
		{
			BufferOptions = new Channels.BufferOptions
			{
				OutboundBufferMaxSize = MaxExportSize
			},
			DisableDiagnostics = DisableDiagnostics,
			UseReadOnlyMemory = UseReadOnlyMemory,
			IndexFormat = "stock-data-v8",
			OutboundChannelExitedCallback = () => _waitHandle.Set(),
#if DEBUG
			ExportResponseCallback = (response, a) =>
			{
				Interlocked.Add(ref _responses, a.Count);
				Console.WriteLine(_responses);
			},
			PublishToOutboundChannelCallback = () => Console.WriteLine("PUBLISHED")
#endif
		};
#pragma warning restore CS0618 // Type or member is obsolete
	}

	[Benchmark]
	public void BulkAll()
	{
		var channel = new IndexChannel<StockData>(_options!);

		channel.TryWriteMany(_data);
		channel.TryComplete();

		_waitHandle.WaitOne();
	}
}
