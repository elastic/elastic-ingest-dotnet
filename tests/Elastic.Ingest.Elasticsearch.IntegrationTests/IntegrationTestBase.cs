using System;
using System.Linq;
using Elastic.Clients.Elasticsearch;
using Elastic.Elasticsearch.Xunit;
using Elastic.Elasticsearch.Xunit.XunitPlumbing;
using Elastic.Transport;
using Xunit.Abstractions;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

public abstract class IntegrationTestBase : IClusterFixture<IngestionCluster>
{
	private readonly ITestOutputHelper _output;

	protected ElasticsearchClient Client { get; }

	protected IntegrationTestBase(IngestionCluster cluster, ITestOutputHelper output)
	{
		_output = output;
		Client = cluster.CreateClient(output);
	}
}
