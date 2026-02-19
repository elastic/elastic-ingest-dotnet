// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Clients.Elasticsearch;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

public abstract class IntegrationTestBase(IngestionCluster cluster) : IntegrationTestBase<IngestionCluster>(cluster);

public abstract class IntegrationTestBase<TCluster>(TCluster cluster)
	where TCluster : IngestionCluster
{
	protected TCluster Cluster { get; } = cluster;
	protected ElasticsearchClient Client => Cluster.Client;
}
