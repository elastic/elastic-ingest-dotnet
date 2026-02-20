// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Transport;
using FluentAssertions;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.DataStreams;

/*
 * Use case: Data streams  (https://elastic.github.io/elastic-ingest-dotnet/index-management/data-streams)
 * Tests:    End-to-end document ingestion into a data stream
 *
 * Document: ServerMetricsEvent (Elastic.Mapping)
 *   Entity: DataStream  "logs-srvmetrics-default"
 *
 *   ┌────────────────────────────────────────────────────────┐
 *   │  IngestChannel<ServerMetricsEvent>                     │
 *   │  ├── Bootstrap templates                               │
 *   │  ├── TryWrite(event) ─→ _bulk to data stream          │
 *   │  └── Verify via _search on logs-srvmetrics-default     │
 *   └────────────────────────────────────────────────────────┘
 *
 * Writes to:     logs-srvmetrics-default  (auto-created data stream)
 * Verifies:      document content via _search, data stream existence via GET _data_stream
 */
[NotInParallel("data-streams")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class IngestionTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
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
		var options = new IngestChannelOptions<ServerMetricsEvent>(Transport, ctx)
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

		var refresh = await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{DsName}/_refresh");
		refresh.ApiCallDetails.HttpStatusCode.Should().Be(200);

		var search = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{DsName}/_search");
		search.ApiCallDetails.HttpStatusCode.Should().Be(200);
		search.Body.Should().Contain("\"product-api\"");
		search.Body.Should().Contain("GET /api/products 200 OK");

		var getDs = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_data_stream/{DsName}");
		getDs.ApiCallDetails.HttpStatusCode.Should().Be(200);
		getDs.Body.Should().Contain(Prefix);
	}
}
