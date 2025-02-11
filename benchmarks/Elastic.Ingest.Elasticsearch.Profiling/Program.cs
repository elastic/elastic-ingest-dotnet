// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.Indices;
using Performance.Common;
using Elastic.Transport;
using JetBrains.Profiler.Api;

#if DEBUG
long responses = 0;
#endif

var disableDiagnostics = true;

var waitHandle = new ManualResetEvent(false);
var stockData = StockData.CreateSampleData(100_000);

var cloudId = Environment.GetEnvironmentVariable("APM_CLOUD_ID");
var elasticPassword = Environment.GetEnvironmentVariable("APM_CLOUD_PASSWORD");

if (string.IsNullOrEmpty(cloudId) || string.IsNullOrEmpty(elasticPassword))
		throw new Exception("Missing environment variables for cloud connection");

MemoryProfiler.ForceGc();
MemoryProfiler.CollectAllocations(true);

var configuration = new TransportConfigurationDescriptor(cloudId, new BasicAuthentication("elastic", elasticPassword))
	.ServerCertificateValidationCallback((a,b,c,d) => true); // Trust the local certificate if we're passing through Fiddler with SSL decryption

var transport = new DistributedTransport(configuration);

MemoryProfiler.GetSnapshot("Before");

var channelOptions = new IndexChannelOptions<StockData>(transport)
{
	BufferOptions = new BufferOptions { OutboundBufferMaxSize = 5_000 },
	DisableDiagnostics = disableDiagnostics,
	IndexFormat = "stock-data-v8",
	OutboundChannelExitedCallback = () =>
	{
		waitHandle.Set();
	},
#if DEBUG
	ExportResponseCallback = (response, a) =>
	{
		Interlocked.Add(ref responses, a.Count);
		Console.WriteLine(responses);
	},
	PublishToOutboundChannelCallback = () => Console.WriteLine("PUBLISHED")
#endif
};

var indexChannel = new IndexChannel<StockData>(channelOptions);

MemoryProfiler.GetSnapshot("Before write");

#if DEBUG
Console.WriteLine("Write data.");
#endif

indexChannel.TryWriteMany(stockData);
indexChannel.TryComplete();

#if DEBUG
Console.WriteLine("Awaiting completion.");
#endif

waitHandle.WaitOne();

#if DEBUG
Console.WriteLine("Completed.");
#endif

MemoryProfiler.GetSnapshot("After write and flush");

if (!disableDiagnostics)
{
	var diagnostics = indexChannel.ToString();
	MemoryProfiler.GetSnapshot("After diagnostics");
}
