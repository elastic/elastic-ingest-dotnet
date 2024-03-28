// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Transport;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

public class BootstrapFailureTests : IntegrationTestBase<SecurityCluster>
{
	public BootstrapFailureTests(SecurityCluster cluster, ITestOutputHelper output) : base(cluster, output)
	{
	}

	[Fact]
	public async Task BootstrapSilentShouldReportError()
	{
		var targetDataStream = new DataStreamName("logs", "silent-failure");
		var slim = new CountdownEvent(1);
		var transport = new DistributedTransport(new TransportConfiguration(Cluster.NodesUris().First()));
		var options = new DataStreamChannelOptions<TimeSeriesDocument>(transport)
		{
			DataStream = targetDataStream,
			BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 1 }
		};
		var channel = new DataStreamChannel<TimeSeriesDocument>(options);
		var bootstrapped = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Silent, "7-days-default");
		bootstrapped.Should().BeFalse("Insufficient rights");
	}

	[Fact]
	public async Task BootstrapFailureShouldReportError()
	{
		var targetDataStream = new DataStreamName("logs", "exception-failure");
		var slim = new CountdownEvent(1);
		var transport = new DistributedTransport(new TransportConfiguration(Cluster.NodesUris().First()));
		var options = new DataStreamChannelOptions<TimeSeriesDocument>(transport)
		{
			DataStream = targetDataStream,
			BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 1 }
		};
		var channel = new DataStreamChannel<TimeSeriesDocument>(options);
		Exception? caughtException = null;
		try
		{
			await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure, "7-days-default");
		}
		catch (Exception e)
		{
			caughtException = e;
		}
		caughtException.Should().NotBeNull();

		caughtException!.Message.Should().StartWith("Failure to create component template `logs-exception-failure-settings` for logs-exception-failure-*:");
		caughtException.Message.Should().Contain("Could not authenticate with the specified node. Try verifying your credentials or check your Shield configuration.");
		caughtException.Message.Should().Contain("Invalid Elasticsearch response built from a unsuccessful (401) low level call on PUT:");
	}
}
