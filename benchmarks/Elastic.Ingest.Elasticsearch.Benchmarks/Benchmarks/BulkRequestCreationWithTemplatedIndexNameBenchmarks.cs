// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Ingest.Elasticsearch.Serialization;
using Performance.Common;

namespace Elastic.Ingest.Elasticsearch.Benchmarks.Benchmarks;

public class BulkRequestCreationWithTemplatedIndexNameBenchmarks
{
	private static readonly int DocumentsToIndex = 1_000;

	private IndexChannelOptions<StockData>? _options;
	private ITransport? _transport;
	private TransportConfiguration? _transportConfiguration;
	private StockData[] _data = Array.Empty<StockData>();

	public Stream MemoryStream { get; } = new MemoryStream();

	[GlobalSetup]
	public void Setup()
	{
		_transportConfiguration = new TransportConfiguration(
				new SingleNodePool(new("http://localhost:9200")),
				new InMemoryRequestInvoker(StockData.CreateSampleDataSuccessWithFilterPathResponseBytes(DocumentsToIndex)));

		_transport = new DistributedTransport(_transportConfiguration);

		_options = new IndexChannelOptions<StockData>(_transport)
		{
			BufferOptions = new Channels.BufferOptions
			{
				OutboundBufferMaxSize = DocumentsToIndex
			}
		};

		_data = StockData.CreateSampleData(DocumentsToIndex);
	}

	[Benchmark(Baseline = true)]
	public async Task DynamicIndexName_WriteToStreamAsync()
	{
		MemoryStream.Position = 0;
		var bytes = BulkRequestDataFactory.GetBytes(_data, _options!, e => BulkRequestDataFactory.CreateBulkOperationHeaderForIndex(e, _options!, false));
		var postData = PostData.ReadOnlyMemory(bytes);
		await postData.WriteAsync(MemoryStream, _transportConfiguration!, false, CancellationToken.None);
	}
}
