// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Elasticsearch;
using Elastic.Elasticsearch.Xunit.XunitPlumbing;
using Xunit.Abstractions;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

public abstract class IntegrationTestBase : IntegrationTestBase<IngestionCluster>
{
	protected IntegrationTestBase(IngestionCluster cluster, ITestOutputHelper output) : base(cluster, output) { }
}
public abstract class IntegrationTestBase<TCluster> : IClusterFixture<TCluster>
	where TCluster : IngestionCluster, new()
{
	protected IngestionCluster Cluster { get; }
	protected ElasticsearchClient Client { get; }


	protected IntegrationTestBase(IngestionCluster cluster, ITestOutputHelper output)
	{
		Cluster = cluster;
		Client = cluster.CreateClient(output);
	}
}
