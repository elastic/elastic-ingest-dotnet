// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Linq;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Bootstrap;

/*
 * Tests: Bootstrap error handling with insufficient privileges
 *
 * Uses SecurityCluster (trial security enabled) but connects with an
 * UNAUTHENTICATED transport — no API key, no basic auth.
 *
 *   IngestChannel<ServerMetricsEvent>
 *   └── BootstrapElasticsearchAsync
 *       ├── Silent  → returns false (no exception)
 *       └── Failure → throws with "Failure to create component template"
 *
 * No indices, templates, or data streams are actually created.
 */
[ClassDataSource<SecurityCluster>(Shared = SharedType.Keyed, Key = nameof(SecurityCluster))]
public class BootstrapFailureTests(SecurityCluster cluster) : IntegrationTestBase<SecurityCluster>(cluster)
{
	[Test]
	public async Task SilentShouldReturnFalse()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var transport = new DistributedTransport(new TransportConfiguration(Cluster.NodesUris().First()));
		var options = new IngestChannelOptions<ServerMetricsEvent>(transport, ctx)
		{
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<ServerMetricsEvent>(options);

		var result = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Silent);
		result.Should().BeFalse("insufficient rights on unauthenticated transport");
	}

	[Test]
	public async Task FailureShouldThrow()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var transport = new DistributedTransport(new TransportConfiguration(Cluster.NodesUris().First()));
		var options = new IngestChannelOptions<ServerMetricsEvent>(transport, ctx)
		{
			BufferOptions = new BufferOptions { OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<ServerMetricsEvent>(options);

		Exception? caught = null;
		try
		{
			await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		}
		catch (Exception e)
		{
			caught = e;
		}

		caught.Should().NotBeNull();
		caught.Message.Should().Contain("Failure to create component template");
	}
}
