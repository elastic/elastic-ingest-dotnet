// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Elasticsearch;
using Elastic.Elasticsearch.Xunit.XunitPlumbing;
using Xunit.Abstractions;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

public abstract class IntegrationTestBase(IngestionCluster cluster, ITestOutputHelper output, string? hostName = null)
	: IntegrationTestBase<IngestionCluster>(cluster, output, hostName);

public abstract class IntegrationTestBase<TCluster>(TCluster cluster, ITestOutputHelper output, string? hostName = null)
	: IClusterFixture<TCluster>
	where TCluster : IngestionCluster, new()
{
	protected IngestionCluster Cluster { get; } = cluster;
	protected ElasticsearchClient Client { get; } = cluster.CreateClient(output, hostName);
}
