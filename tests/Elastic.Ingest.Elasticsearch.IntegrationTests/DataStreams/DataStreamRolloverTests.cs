// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.Strategies;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.DataStreams;

/*
 * Tests: Manual rollover of a data stream backing index
 *
 * Document: ServerMetricsEvent (Elastic.Mapping)
 *   Entity: DataStream  "logs-srvmetrics-default"
 *
 *   ┌──────────────────────────────────────────────────────────────┐
 *   │  1. Bootstrap + write 1 doc → backing index 000001           │
 *   │  2. ManualRolloverStrategy.RolloverAsync(target)             │
 *   │     └── POST /logs-srvmetrics-default/_rollover              │
 *   │  3. GET /_data_stream/logs-srvmetrics-default                │
 *   │     └── verify backing index 000002 exists                   │
 *   └──────────────────────────────────────────────────────────────┘
 *
 * Writes to:    logs-srvmetrics-default
 * After rollover: .ds-logs-srvmetrics-default-000001 (old)
 *                 .ds-logs-srvmetrics-default-000002 (new write target)
 */
[NotInParallel("logs-srvmetrics")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class DataStreamRolloverTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string Prefix = "logs-srvmetrics";
	private const string DsName = "logs-srvmetrics-default";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync(Prefix);

	[After(Test)]
	public async Task Teardown() => await CleanupPrefixAsync(Prefix);

	[Test]
	public async Task ManualRolloverCreatesNewBackingIndex()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var slim = new CountdownEvent(1);
		var options = new IngestChannelOptions<ServerMetricsEvent>(Transport, ctx)
		{
			BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<ServerMetricsEvent>(options);

		(await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();

		channel.TryWrite(new ServerMetricsEvent
		{
			Timestamp = DateTimeOffset.UtcNow,
			Message = "Pre-rollover event",
			LogLevel = "info",
			ServiceName = "rollover-test",
			HostName = "web-01",
			TraceId = "roll-001",
			DurationMs = 10
		});
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("Pre-rollover write timed out");

		var rollover = new ManualRolloverStrategy();
		var rolloverCtx = new RolloverContext
		{
			Transport = Transport,
			Target = DsName
		};
		var rolled = await rollover.RolloverAsync(rolloverCtx);
		rolled.Should().BeTrue();

		var dsResponse = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_data_stream/{DsName}");
		dsResponse.ApiCallDetails.HttpStatusCode.Should().Be(200);
		dsResponse.Body.Should().Contain("000002",
			"after rollover, data stream should have a second backing index");
	}
}
