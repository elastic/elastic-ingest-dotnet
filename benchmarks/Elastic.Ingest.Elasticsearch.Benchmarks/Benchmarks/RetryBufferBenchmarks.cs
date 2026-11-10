// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using Elastic.Channels.Buffers;
using Elastic.Ingest.Elasticsearch.Serialization;
using Performance.Common;

namespace Elastic.Ingest.Elasticsearch.Benchmarks;

/// <summary>
/// What the channel does with the response of one export of 5,000 documents, the step that decides which documents are retried
/// or rejected. Called directly, so the numbers are that step alone.
/// </summary>
[MemoryDiagnoser]
public class RetryBufferBenchmarks : IDisposable
{
	private const int Count = 5_000;

	/// <summary>Exposes the protected step.</summary>
	private sealed class ProbeChannel(IndexChannelOptions<StockData> options) : IndexChannel<StockData>(options)
	{
		public ArraySegment<StockData> Decide(BulkResponse response, ArraySegment<StockData> page) => RetryBuffer(response, page, null!);
	}

	private ProbeChannel _channel = null!;
	private ArraySegment<StockData> _page;
	private BulkResponse _everythingSucceeded = null!;
	private BulkResponse _someFailed = null!;

	[GlobalSetup]
	public void Setup()
	{
		_page = new ArraySegment<StockData>(StockData.CreateSampleData(Count));

		BulkResponse Respond(string body)
		{
			var transport = new DistributedTransport(new TransportConfiguration(new SingleNodePool(new("http://localhost:9200")),
				new InMemoryRequestInvoker(Encoding.UTF8.GetBytes(body))));
			return transport.Request<BulkResponse>(Elastic.Transport.HttpMethod.POST, "_bulk", PostData.String(""));
		}

		var ok = string.Join(",", Enumerable.Repeat("{\"create\":{\"status\":201}}", Count));
		_everythingSucceeded = Respond("{\"errors\":false,\"items\":[" + ok + "]}");

		// five percent of the items fail: most of them are rejected, a few are retryable
		var mixed = string.Join(",", Enumerable.Range(0, Count).Select(i =>
			i % 20 != 0 ? "{\"create\":{\"status\":201}}"
			: i % 200 == 0 ? "{\"create\":{\"status\":429,\"error\":{\"type\":\"es_rejected_execution_exception\",\"reason\":\"busy\"}}}"
			: "{\"create\":{\"status\":400,\"error\":{\"type\":\"mapper_parsing_exception\",\"reason\":\"bad\"}}}"));
		_someFailed = Respond("{\"errors\":true,\"items\":[" + mixed + "]}");

		_channel = new ProbeChannel(new IndexChannelOptions<StockData>(new DistributedTransport(
			new TransportConfiguration(new SingleNodePool(new("http://localhost:9200")), new InMemoryRequestInvoker())))
		{
			IndexFormat = "stock-data-v8",
			DisableDiagnostics = true,
			ServerRejectionCallback = _ => { }
		});
	}

	[GlobalCleanup]
	public void Cleanup() => Dispose();

	public void Dispose() => _channel.Dispose();

	[Benchmark(Baseline = true)]
	public int EverythingSucceeded() => _channel.Decide(_everythingSucceeded, _page).Count;

	[Benchmark]
	public int FivePercentFailed() => _channel.Decide(_someFailed, _page).Count;
}
