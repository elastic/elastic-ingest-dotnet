// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
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
