// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Ingestion;

[NotInParallel("logs-srvmetrics")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class DataStreamIngestionTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string Prefix = "logs-srvmetrics";
	private const string DsName = "logs-srvmetrics-default";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync(Prefix);

	[After(Test)]
	public async Task Teardown() => await CleanupPrefixAsync(Prefix);

	[Test]
	public async Task EnsureDocumentsEndUpInDataStream()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var slim = new CountdownEvent(1);
		var options = new IngestChannelOptions<ServerMetricsEvent>(Client.Transport, ctx)
		{
			BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<ServerMetricsEvent>(options);

		var bootstrapped = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		bootstrapped.Should().BeTrue();

		channel.TryWrite(new ServerMetricsEvent
		{
			Timestamp = DateTimeOffset.UtcNow,
			Message = "GET /api/products 200 OK",
			LogLevel = "info",
			ServiceName = "product-api",
			HostName = "web-01",
			TraceId = "abc-123",
			DurationMs = 42
		});

		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"Document was not persisted within 10 seconds: {channel}");

		var refresh = await Client.Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{DsName}/_refresh");
		refresh.ApiCallDetails.HttpStatusCode.Should().Be(200);

		var search = await Client.Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{DsName}/_search");
		search.ApiCallDetails.HttpStatusCode.Should().Be(200);
		search.Body.Should().Contain("\"product-api\"");
		search.Body.Should().Contain("GET /api/products 200 OK");

		var getDs = await Client.Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_data_stream/{DsName}");
		getDs.ApiCallDetails.HttpStatusCode.Should().Be(200);
		getDs.Body.Should().Contain(Prefix);
	}
}
