// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.Strategies;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.DataStreams;

/*
 * Use case: Data streams  (https://elastic.github.io/elastic-ingest-dotnet/index-management/data-streams)
 * Tests:    Template bootstrap — component templates, data stream template, hash idempotency
 *
 * Document: ServerMetricsEvent (Elastic.Mapping)
 *   Entity: DataStream  Type="logs"  Dataset="srvmetrics"  Namespace="default"
 *
 *   BootstrapElasticsearchAsync(Failure)
 *   ├── PUT /_component_template/logs-srvmetrics-settings   (analysis: log_message)
 *   ├── PUT /_component_template/logs-srvmetrics-mappings   (@timestamp, message, ...)
 *   └── PUT /_index_template/logs-srvmetrics                (data_stream: {})
 *
 * Mapping update mechanism:
 *   Template updates only affect new backing indices.
 *   A rollover (manual or automatic) is needed to pick up updated templates.
 *   See MappingEvolutionTests for this scenario.
 */
[NotInParallel("data-streams")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class BootstrapTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string Prefix = "logs-srvmetrics";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync(Prefix);

	[After(Test)]
	public async Task Teardown() => await CleanupPrefixAsync(Prefix);

	[Test]
	public async Task BootstrapCreatesComponentAndDataStreamTemplates()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var options = new IngestChannelOptions<ServerMetricsEvent>(Transport, ctx)
		{
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<ServerMetricsEvent>(options);

		var result = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		result.Should().BeTrue();

		var settingsTemplate = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_component_template/{Prefix}-settings");
		settingsTemplate.ApiCallDetails.HttpStatusCode.Should().Be(200);

		var mappingsTemplate = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_component_template/{Prefix}-mappings");
		mappingsTemplate.ApiCallDetails.HttpStatusCode.Should().Be(200);
		mappingsTemplate.Body.Should().Contain("\"@timestamp\"");
		mappingsTemplate.Body.Should().Contain("\"message\"");
		mappingsTemplate.Body.Should().Contain("\"duration_ms\"");

		var indexTemplate = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_index_template/{Prefix}");
		indexTemplate.ApiCallDetails.HttpStatusCode.Should().Be(200);
	}

	[Test]
	public async Task BootstrapIncludesIndexSettingsFromConfiguration()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var options = new IngestChannelOptions<ServerMetricsEvent>(Transport, ctx)
		{
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<ServerMetricsEvent>(options);

		var result = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		result.Should().BeTrue();

		var settingsTemplate = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_component_template/{Prefix}-settings");
		settingsTemplate.ApiCallDetails.HttpStatusCode.Should().Be(200);
		settingsTemplate.Body.Should().Contain("\"index.default_pipeline\"");
		settingsTemplate.Body.Should().Contain("\"logs-default-pipeline\"");
	}

	[Test]
	public async Task RebootstrapWithSameHashIsIdempotent()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var options = new IngestChannelOptions<ServerMetricsEvent>(Transport, ctx)
		{
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};

		var channel1 = new IngestChannel<ServerMetricsEvent>(options);
		(await channel1.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();
		var hash1 = channel1.ChannelHash;

		var channel2 = new IngestChannel<ServerMetricsEvent>(options);
		(await channel2.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();
		var hash2 = channel2.ChannelHash;

		hash1.Should().Be(hash2, "identical mappings should produce the same hash");
	}

	[Test]
	public async Task WithRetentionBootstrapsLifecycle()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var strategy = IngestStrategies.DataStream<ServerMetricsEvent>(ctx, "30d");
		var options = new IngestChannelOptions<ServerMetricsEvent>(Transport, strategy, ctx)
		{
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<ServerMetricsEvent>(options);

		var result = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		result.Should().BeTrue("bootstrap with lifecycle retention should succeed");

		var indexTemplate = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_index_template/{Prefix}");
		indexTemplate.ApiCallDetails.HttpStatusCode.Should().Be(200);
	}
}
