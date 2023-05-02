// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading;

namespace Elastic.Ingest.Elasticsearch.Benchmarks;

public class BulkIngestion
{
	private int _responses;

	// NOTE: response includes 5_000 items
	private static readonly byte[] _responseBytes = File.ReadAllBytes($"{Directory.GetCurrentDirectory()}\\SampleData\\BulkIngestResponseSuccess.txt");

	private static TransportConfiguration GetConfig()
	{
		var config = new TransportConfiguration(new SingleNodePool(new("http://localhost:9200")), new InMemoryConnection(_responseBytes));
		//.EnableDebugMode();

		return config;
	}

	private static HttpTransport Transport => new DefaultHttpTransport(GetConfig());

	private readonly ManualResetEvent _waitHandle = new(false);

	private IndexChannelOptions<StockData> ChannelOptions => new(Transport)
	{
		BufferOptions = new Channels.BufferOptions { OutboundBufferMaxSize = 5_000 },
		DisableDiagnostics = true,
		IndexFormat = "stock-data-v8",
		OutboundChannelExitedCallback = () =>
		{
			Console.WriteLine("Outbound channel exiting...");
			_waitHandle.Set();
		},
		ExportResponseCallback = (response, a) =>
		{
			Interlocked.Add(ref _responses, response.Items.Count);
			Console.WriteLine(_responses);
		},
		PublishToOutboundChannelCallback = () => Console.WriteLine("PUBLISHED")		
	};

	private IndexChannel<StockData> Channel => new(ChannelOptions);

	private StockData[] _data = Array.Empty<StockData>();

	[GlobalSetup]
	public void Setup()
	{
		var data = new List<StockData>(100000);
		var file = new StreamReader($"{Directory.GetCurrentDirectory()}\\SampleData\\StockData_100k.csv");

		string? line;

		while ((line = file.ReadLine()) is not null)
			data.Add(StockData.ParseFromFileLine(line));

		_data = data.ToArray();
	}

	[Benchmark]
	public void BulkAll()
	{
		Channel.TryWriteMany(_data);

		// Thread.Sleep(1000);

		Channel.TryComplete();

		_waitHandle.WaitOne();
	}
}
