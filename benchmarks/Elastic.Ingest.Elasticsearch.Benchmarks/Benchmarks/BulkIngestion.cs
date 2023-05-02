// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading;

namespace Elastic.Ingest.Elasticsearch.Benchmarks;

public class BulkIngestion
{
	private int _responses;

	// NOTE: response includes 5_000 items
	private static readonly byte[] _responseBytes =
		File.ReadAllBytes(Path.Combine(Directory.GetCurrentDirectory(), "SampleData", "BulkIngestResponseSuccess.txt"));

	private static TransportConfiguration GetConfig()
	{
		var config = new TransportConfiguration(new SingleNodePool(new("http://localhost:9200")), new InMemoryConnection(_responseBytes));
		//.EnableDebugMode();

		return config;
	}

	private static HttpTransport Transport => new DefaultHttpTransport(GetConfig());

	private readonly ManualResetEvent _waitHandle = new(false);

	private IndexChannelOptions<StockData>? ChannelOptions { get; set; }
	private IndexChannel<StockData>? Channel { get; set; }

	private static long _totalEventsToPublish = 100_000;
	private static long _maxExportSize = 5_000;
	private StockData[] _data = Array.Empty<StockData>();

	[GlobalSetup]
	public void Setup()
	{
		ChannelOptions = new IndexChannelOptions<StockData>(Transport)
		{
			BufferOptions = new Channels.BufferOptions { OutboundBufferMaxSize = 5_000, InboundBufferMaxSize = (int)_totalEventsToPublish },
			DisableDiagnostics = true,
			IndexFormat = "stock-data-v8",
			OutboundChannelExitedCallback = () =>
			{
				Console.WriteLine("Outbound channel exiting...");
				_waitHandle.Set();
			},
			ExportResponseCallback = (r, a) =>
			{
				var response = Interlocked.Increment(ref _responses);
				Console.WriteLine($"Response: {response} / {_totalEventsToPublish / _maxExportSize}. Buffer size: {a.Count}");
			},
			PublishToOutboundChannelCallback = () => Console.WriteLine("PUBLISHED")
		};
		Channel = new IndexChannel<StockData>(ChannelOptions);

		var data = new List<StockData>(100_000);
		var file = new StreamReader(Path.Combine(Directory.GetCurrentDirectory(), "SampleData", "StockData_100k.csv"));

		string? line;

		while ((line = file.ReadLine()) is not null)
			data.Add(StockData.ParseFromFileLine(line));

		_data = data.ToArray();
	}

	[Benchmark]
	public async Task BulkAll()
	{
		Console.WriteLine($"Attempting to write {_data.Length:N0} items");
		foreach (var d in _data)
		{
			var written = false;
			var ready = await Channel!.WaitToWriteAsync();
			if (ready) written = Channel.TryWrite(d);
			if (!written)
				throw new Exception($"Could not write {d.Name}");
		}
		// Thread.Sleep(1000);
		Console.WriteLine($"Attempting to complete channel");

		Channel!.TryComplete();

		_waitHandle.WaitOne();
	}
}
