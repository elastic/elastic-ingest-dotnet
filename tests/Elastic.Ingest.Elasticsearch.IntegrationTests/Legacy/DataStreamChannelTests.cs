// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Legacy;

/*
 * Tests: Legacy DataStreamChannel<T> API (pre-IngestStrategy)
 *
 * Unlike IngestChannel<T> which delegates to IIngestStrategy, the older
 * DataStreamChannel<T> owns its own bootstrap and bulk routing directly.
 *
 * Document: ServerMetricsEvent
 *   DataStream: logs-dschannel-default
 *
 * Bootstrap (DataStreamChannel):
 *   BootstrapElasticsearchAsync(Failure)
 *   ├── PUT /_component_template/logs-dschannel-settings       (default settings)
 *   ├── PUT /_component_template/logs-dschannel-mappings       (default mappings, no custom analysis)
 *   └── PUT /_index_template/logs-dschannel                    (data_stream: {}, composed_of: [logs-*])
 *
 *   ┌─────────────────────────────────────────────────────────┐
 *   │  DataStreamChannel<ServerMetricsEvent>                   │
 *   │  ├── Bootstrap templates (logs-dschannel)                │
 *   │  ├── TryWrite(event) ─→ _bulk to logs-dschannel-default │
 *   │  ├── Verify via _search on data stream                   │
 *   │  └── GET /_data_stream/logs-dschannel-default            │
 *   └─────────────────────────────────────────────────────────┘
 *
 * No custom analysis:  The old channel API does not inject Elastic.Mapping analysis;
 *                      it uses default component settings only.
 */
[NotInParallel("legacy-channels")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class DataStreamChannelTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string Prefix = "logs-dschannel";
	private const string DsName = "logs-dschannel-default";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync(Prefix);

	[Test]
	public async Task BootstrapAndIngestViaLegacyDataStreamChannel()
	{
		var slim = new CountdownEvent(1);
		var options = new DataStreamChannelOptions<ServerMetricsEvent>(Transport)
		{
			DataStream = new DataStreamName("logs", "dschannel", "default"),
			BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 1 }
		};
		var channel = new DataStreamChannel<ServerMetricsEvent>(options);

		var bootstrapped = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		bootstrapped.Should().BeTrue("DataStreamChannel bootstrap should succeed");

		channel.TryWrite(new ServerMetricsEvent
		{
			Timestamp = DateTimeOffset.UtcNow,
			Message = "Legacy channel test event",
			LogLevel = "info",
			ServiceName = "legacy-ds-test",
			HostName = "host-01",
			TraceId = "legacy-001",
			DurationMs = 7
		});

		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"Document was not persisted within 10 seconds: {channel}");

		var refresh = await Transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"/{DsName}/_refresh");
		refresh.ApiCallDetails.HttpStatusCode.Should().Be(200);

		var search = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{DsName}/_search");
		search.ApiCallDetails.HttpStatusCode.Should().Be(200);
		search.Body.Should().Contain("\"legacy-ds-test\"");
		search.Body.Should().Contain("Legacy channel test event");

		var getDs = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_data_stream/{DsName}");
		getDs.ApiCallDetails.HttpStatusCode.Should().Be(200);
		getDs.Body.Should().Contain(Prefix);
	}

	[Test]
	public async Task ChannelHashIsStableAcrossIdenticalChannels()
	{
		var options = new DataStreamChannelOptions<ServerMetricsEvent>(Transport)
		{
			DataStream = new DataStreamName("logs", "dschannel", "default"),
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};

		var ch1 = new DataStreamChannel<ServerMetricsEvent>(options);
		(await ch1.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();
		var hash1 = ch1.ChannelHash;

		var ch2 = new DataStreamChannel<ServerMetricsEvent>(options);
		(await ch2.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();
		var hash2 = ch2.ChannelHash;

		hash1.Should().Be(hash2, "identical DataStreamChannel options should produce the same hash");
	}
}
