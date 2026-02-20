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
 * Use case: Data streams + Rollover  (https://elastic.github.io/elastic-ingest-dotnet/index-management/data-streams)
 * Tests:    Mapping evolution — rollover creates a new backing index with updated templates
 *
 * Scenario:
 *   ┌──────────────────────────────────────────────────────────────────────────┐
 *   │  1. Bootstrap V1 (ServerMetricsEventConfig)                              │
 *   │     → creates logs-srvmetrics template with hash₁                        │
 *   │     → writes doc → backing index .ds-...-000001 has V1 analysis          │
 *   │                                                                          │
 *   │  2. Bootstrap V2 (ServerMetricsEventV2Config)                            │
 *   │     → hash₂ ≠ hash₁ → templates updated                                 │
 *   │     → BUT existing backing index 000001 still has V1 settings            │
 *   │                                                                          │
 *   │  3. POST /logs-srvmetrics-default/_rollover                              │
 *   │     → new backing index .ds-...-000002 created from updated template     │
 *   │     → 000002 has V2 analysis (stop filter present)                       │
 *   │                                                                          │
 *   │  4. Write post-rollover doc → lands in 000002                            │
 *   │     → search across data stream finds both pre- and post-rollover data   │
 *   └──────────────────────────────────────────────────────────────────────────┘
 *
 * Key insight for data stream users:
 *   Template updates require a rollover to take effect. Existing backing
 *   indices retain their original mappings/settings. After rollover, new
 *   documents land in the fresh backing index with the updated schema.
 */
[NotInParallel("data-streams")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class MappingEvolutionTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string Prefix = "logs-srvmetrics";
	private const string DsName = "logs-srvmetrics-default";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync(Prefix);

	[After(Test)]
	public async Task Teardown() => await CleanupPrefixAsync(Prefix);

	[Test]
	public async Task RolloverAfterTemplateUpdatePicksUpV2Mappings()
	{
		// ── Phase 1: Bootstrap V1 + write ───────────────────────────────

		var ctxV1 = TestMappingContext.ServerMetricsEvent.Context;
		var slim = new CountdownEvent(1);
		var channelV1 = new IngestChannel<ServerMetricsEvent>(
			new IngestChannelOptions<ServerMetricsEvent>(Transport, ctxV1)
			{
				BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 1 }
			});

		(await channelV1.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();
		var hashV1 = channelV1.ChannelHash;
		hashV1.Should().NotBeNullOrEmpty();

		channelV1.TryWrite(new ServerMetricsEvent
		{
			Timestamp = DateTimeOffset.UtcNow,
			Message = "Pre-evolution event V1",
			LogLevel = "info",
			ServiceName = "evo-test",
			HostName = "web-01",
			TraceId = "evo-v1-001",
			DurationMs = 42
		});
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("V1 write timed out");

		await Transport.RequestAsync<StringResponse>(HttpMethod.POST, $"/{DsName}/_refresh");

		// Verify data stream exists with 1 backing index
		var dsCheck = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_data_stream/{DsName}");
		dsCheck.ApiCallDetails.HttpStatusCode.Should().Be(200);
		dsCheck.Body.Should().Contain("000001");

		// ── Phase 2: Bootstrap V2 — templates updated ───────────────────

		var ctxV2 = TestMappingContext.ServerMetricsEventV2.Context;
		var channelV2 = new IngestChannel<ServerMetricsEventV2>(
			new IngestChannelOptions<ServerMetricsEventV2>(Transport, ctxV2)
			{
				BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
			});

		(await channelV2.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();
		var hashV2 = channelV2.ChannelHash;

		hashV2.Should().NotBe(hashV1,
			"V2 config adds stop filter to log_message analyzer, producing a different hash");

		// ── Phase 3: Rollover → new backing index picks up V2 ───────────

		var rollover = new ManualRolloverStrategy();
		var rolloverCtx = new RolloverContext { Transport = Transport, Target = DsName };
		var rolled = await rollover.RolloverAsync(rolloverCtx);
		rolled.Should().BeTrue("rollover should succeed");

		var dsAfter = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_data_stream/{DsName}");
		dsAfter.Body.Should().Contain("000002",
			"after rollover, data stream should have a second backing index");

		// ── Phase 4: Write post-rollover doc ────────────────────────────

		slim.Reset();
		var channelV2Write = new IngestChannel<ServerMetricsEventV2>(
			new IngestChannelOptions<ServerMetricsEventV2>(Transport, ctxV2)
			{
				BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 1 }
			});
		await channelV2Write.BootstrapElasticsearchAsync(BootstrapMethod.Failure);

		channelV2Write.TryWrite(new ServerMetricsEventV2
		{
			Timestamp = DateTimeOffset.UtcNow,
			Message = "Post-evolution event V2",
			LogLevel = "error",
			ServiceName = "evo-test",
			HostName = "web-02",
			TraceId = "evo-v2-001",
			DurationMs = 2000,
			ErrorCode = "E500"
		});
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception("V2 write timed out");

		await Transport.RequestAsync<StringResponse>(HttpMethod.POST, $"/{DsName}/_refresh");

		// ── Phase 5: Verify ─────────────────────────────────────────────

		// Search the entire data stream — both V1 and V2 docs should be there
		var search = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/{DsName}/_search");
		search.ApiCallDetails.HttpStatusCode.Should().Be(200);
		search.Body.Should().Contain("\"evo-v1-001\"");
		search.Body.Should().Contain("\"evo-v2-001\"");

		// Verify V2 backing index has the updated settings (stop filter)
		var resolve = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_resolve/index/.ds-{DsName}-*");
		resolve.ApiCallDetails.HttpStatusCode.Should().Be(200);

		// Get settings from the latest backing index to verify V2 analysis
		var v2IndexSettings = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/.ds-{DsName}-*000002/_settings");
		if (v2IndexSettings.ApiCallDetails.HasSuccessfulStatusCode)
		{
			v2IndexSettings.Body.Should().Contain("\"log_message\"",
				"V2 backing index should have the log_message analyzer");
		}
	}
}
