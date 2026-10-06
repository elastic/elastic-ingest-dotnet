// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Ingest.Elasticsearch.Bulk;
using Elastic.Ingest.Elasticsearch.Serialization;
using Performance.Common;

namespace Elastic.Ingest.Elasticsearch.Benchmarks.Benchmarks;

[JsonSerializable(typeof(StockData))]
internal sealed partial class StockDataContext : JsonSerializerContext;

/// <summary>
/// One 1,000 document _bulk request end to end against an in memory transport:
/// the channel-free <see cref="BulkSender{TItem,TBody}"/> versus the serialize-then-send path the channel uses
/// and <c>DirectWriteAsync</c> (which needs a full channel).
/// </summary>
[MemoryDiagnoser]
public class BulkSenderBenchmarks : IDisposable
{
	private const int DocumentsToIndex = 1_000;

	private ITransport? _transport;
	private IndexChannelOptions<StockData>? _options;
	private IndexChannel<StockData>? _channel;
	private BulkSender<StockData, StockData>? _sender;
	private StockData[] _data = Array.Empty<StockData>();
	private List<StockData> _list = new();

	[GlobalSetup]
	public void Setup()
	{
		var configuration = new TransportConfiguration(
			new SingleNodePool(new("http://localhost:9200")),
			new InMemoryRequestInvoker(StockData.CreateSampleDataSuccessWithFilterPathResponseBytes(DocumentsToIndex)));
		_transport = new DistributedTransport(configuration);
		_data = StockData.CreateSampleData(DocumentsToIndex);
		_list = _data.ToList();

		_options = new IndexChannelOptions<StockData>(_transport)
		{
			IndexFormat = "stock-data-v8",
			SerializerContext = StockDataContext.Default
		};
		_channel = new IndexChannel<StockData>(_options);
		_sender = BulkSender.Create(_transport, StockDataContext.Default.StockData, static _ => BulkAction.Create(), "stock-data-v8");
	}

	[Benchmark(Baseline = true)]
	public async Task<BulkResponse> GetBytesThenRequest()
	{
		var bytes = BulkRequestDataFactory.GetBytes(new ArraySegment<StockData>(_data), _options!, static _ => BulkAction.Create());
		return await _transport!.RequestAsync<BulkResponse>(Elastic.Transport.HttpMethod.POST, "stock-data-v8/_bulk", PostData.ReadOnlyMemory(bytes));
	}

	[Benchmark]
	public Task<BulkResponse> DirectWrite() => _channel!.DirectWriteAsync(_data);

	[Benchmark]
	public Task<BulkResponse> BulkSenderArray() => _sender!.SendAsync(_data);

	[Benchmark]
	public Task<BulkResponse> BulkSenderListSpan() =>
		_sender!.SendAsync(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_list));

	// the canned response describes 1,000 items, so a single batch keeps this an overhead measurement of the pull wrapper
	[Benchmark]
	public Task<BulkIngestAllResult> IngestAllOneBatch() => _sender!.IngestAllAsync(_data, new IngestAllOptions { BatchSize = DocumentsToIndex, MaxConcurrency = 1 });

	public void Dispose() => _channel?.Dispose();
}
