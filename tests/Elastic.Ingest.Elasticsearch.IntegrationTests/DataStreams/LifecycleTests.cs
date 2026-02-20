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
 * Use case: Data stream lifecycle  (https://elastic.github.io/elastic-ingest-dotnet/index-management/rollover/data-stream-lifecycle)
 * Tests:    Verify that lifecycle retention, analysis, and ILM are correctly embedded in templates
 *
 * DataStreamLifecycleStep stores the retention period (e.g. "30d") in
 * BootstrapContext.Properties. The DataStreamTemplateStep reads it and
 * embeds a `"template": { "lifecycle": { "data_retention": "30d" } }`
 * block in the index template body.
 *
 *   BootstrapElasticsearchAsync(Failure) with retention = "30d"
 *   ├── PUT /_component_template/logs-srvmetrics-settings
 *   ├── DataStreamLifecycleStep  → stores retention in context
 *   ├── PUT /_component_template/logs-srvmetrics-mappings  (analysis: log_message)
 *   └── PUT /_index_template/logs-srvmetrics               (data_stream + lifecycle block)
 *
 * Also verifies:
 *   • Settings component template contains the log_message analyzer
 *   • ILM bootstrap creates the ILM policy (non-serverless only)
 */
[NotInParallel("data-streams")]
[ClassDataSource<IngestionCluster>(Shared = SharedType.Keyed, Key = nameof(IngestionCluster))]
public class LifecycleTests(IngestionCluster cluster) : IntegrationTestBase(cluster)
{
	private const string Prefix = "logs-srvmetrics";

	[Before(Test)]
	public async Task Setup() => await CleanupPrefixAsync(Prefix);

	[After(Test)]
	public async Task Teardown() => await CleanupPrefixAsync(Prefix);

	[Test]
	public async Task RetentionPeriodAppearsInTemplateBody()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var strategy = IngestStrategies.DataStream<ServerMetricsEvent>(ctx, "30d");
		var options = new IngestChannelOptions<ServerMetricsEvent>(Transport, strategy, ctx)
		{
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<ServerMetricsEvent>(options);

		(await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();

		var template = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_index_template/{Prefix}");
		template.ApiCallDetails.HttpStatusCode.Should().Be(200);

		template.Body.Should().Contain("\"data_retention\"",
			"data stream template should contain lifecycle data_retention block");
		template.Body.Should().Contain("\"30d\"",
			"retention period should be 30d as configured");
	}

	[Test]
	public async Task SettingsTemplateContainsAnalysis()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var options = new IngestChannelOptions<ServerMetricsEvent>(Transport, ctx)
		{
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<ServerMetricsEvent>(options);

		(await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();

		var mappingsTemplate = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_component_template/{Prefix}-mappings");
		mappingsTemplate.ApiCallDetails.HttpStatusCode.Should().Be(200);

		mappingsTemplate.Body.Should().Contain("\"log_message\"",
			"mappings component template should contain the log_message analyzer reference");
		mappingsTemplate.Body.Should().Contain("\"analysis\"",
			"mappings component template should embed the analysis configuration");
		mappingsTemplate.Body.Should().Contain("\"standard\"",
			"analysis should reference the standard tokenizer");
	}

	[Test]
	public async Task DataStreamTemplateReferencesBuiltInLogComponents()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var options = new IngestChannelOptions<ServerMetricsEvent>(Transport, ctx)
		{
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<ServerMetricsEvent>(options);

		(await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();

		var template = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_index_template/{Prefix}");
		template.ApiCallDetails.HttpStatusCode.Should().Be(200);

		template.Body.Should().Contain("\"data_stream\"",
			"template should have data_stream block");
		template.Body.Should().Contain("\"logs-settings\"",
			"logs type should include the built-in logs-settings component template");
		template.Body.Should().Contain("\"logs-mappings\"",
			"logs type should include the built-in logs-mappings component template");
	}

	[Test]
	public async Task IlmBootstrapCreatesPolicy()
	{
		var ilmCheck = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, "/_ilm/status");
		if (ilmCheck.ApiCallDetails.HttpStatusCode != 200)
			return; // ILM not available on this cluster (e.g. serverless)

		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var strategy = IngestStrategies.DataStream<ServerMetricsEvent>(ctx,
			BootstrapStrategies.DataStreamWithIlm("logs-srvmetrics-policy"));
		var options = new IngestChannelOptions<ServerMetricsEvent>(Transport, strategy, ctx)
		{
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<ServerMetricsEvent>(options);

		(await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure)).Should().BeTrue();

		var ilmResponse = await Transport.RequestAsync<StringResponse>(
			HttpMethod.GET, "/_ilm/policy/logs-srvmetrics-policy");
		ilmResponse.ApiCallDetails.HttpStatusCode.Should().Be(200);
		ilmResponse.Body.Should().Contain("logs-srvmetrics-policy");
	}
}
