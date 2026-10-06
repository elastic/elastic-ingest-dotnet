// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using Elastic.Ingest.Elasticsearch.Serialization;

namespace Elastic.Ingest.Elasticsearch.Benchmarks.Benchmarks;

/// <summary>
/// What reporting <c>_id</c> and <c>_index</c> costs: parsing a 1,000 item <c>_bulk</c> response without them (items are shared
/// instances), with an explicit id and a shared index, and with a server generated id per item.
/// </summary>
[MemoryDiagnoser]
public class BulkItemIdentityBenchmarks : IDisposable
{
	private const int Items = 1_000;

	private ITransport? _plain, _indexOnly, _idOnly, _both;

	private static byte[] Response(bool index, bool id)
	{
		var sb = new StringBuilder("{\"errors\":false,\"items\":[");
		for (var i = 0; i < Items; i++)
		{
			if (i > 0) sb.Append(',');
			sb.Append("{\"create\":{");
			if (index) sb.Append("\"_index\":\".ds-logs-nginx.access-default-2026.10.06-000001\",");
			if (id) sb.Append("\"_id\":\"AXx9kQ3pTzGm").Append(i.ToString("D8", System.Globalization.CultureInfo.InvariantCulture)).Append("\",");
			sb.Append("\"status\":201}}");
		}
		return Encoding.UTF8.GetBytes(sb.Append("]}").ToString());
	}

	private static ITransport Transport(byte[] response) =>
		new DistributedTransport(new TransportConfiguration(new SingleNodePool(new("http://localhost:9200")), new InMemoryRequestInvoker(response)));

	[GlobalSetup]
	public void Setup()
	{
		var plain = Response(index: false, id: false);
		var both = Response(index: true, id: true);
		Console.WriteLine($"// response bytes per 1,000 items: plain={plain.Length:N0} both={both.Length:N0} ({both.Length / (double)plain.Length:N1}x)");
		_plain = Transport(plain);
		_indexOnly = Transport(Response(index: true, id: false));
		_idOnly = Transport(Response(index: false, id: true));
		_both = Transport(both);
	}

	[Benchmark(Baseline = true)]
	public Task<BulkResponse> Plain() => _plain!.RequestAsync<BulkResponse>(Elastic.Transport.HttpMethod.POST, "_bulk");

	[Benchmark]
	public Task<BulkResponse> OnlyIndex() => _indexOnly!.RequestAsync<BulkResponse>(Elastic.Transport.HttpMethod.POST, "_bulk");

	[Benchmark]
	public Task<BulkResponse> OnlyId() => _idOnly!.RequestAsync<BulkResponse>(Elastic.Transport.HttpMethod.POST, "_bulk");

	[Benchmark]
	public Task<BulkResponse> IdAndIndex() => _both!.RequestAsync<BulkResponse>(Elastic.Transport.HttpMethod.POST, "_bulk");

	public void Dispose() { }
}
