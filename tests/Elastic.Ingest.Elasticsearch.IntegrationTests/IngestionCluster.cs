// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Linq;
using Elastic.Clients.Elasticsearch;
using Elastic.Elasticsearch.Ephemeral;
using Elastic.Elasticsearch.Xunit;
using Elastic.Transport;
using Xunit;
using Xunit.Abstractions;
using static Elastic.Elasticsearch.Managed.DetectedProxySoftware;

[assembly: TestFramework("Elastic.Elasticsearch.Xunit.Sdk.ElasticTestFramework", "Elastic.Elasticsearch.Xunit")]

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

/// <summary> Declare our cluster that we want to inject into our test classes </summary>
public class IngestionCluster(XunitClusterConfiguration xunitClusterConfiguration)
	: XunitClusterBase(xunitClusterConfiguration)
{
	protected static readonly string Version = "9.0.0";

	public IngestionCluster() : this(new XunitClusterConfiguration(Version) { StartingPortNumber = 9202 }) { }

	public ElasticsearchClient CreateClient(ITestOutputHelper output, string? hostname = null) =>
		this.GetOrAddClient(cluster =>
		{
			var isCi = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"));
			var nodes = NodesUris(hostname);
			var connectionPool = new StaticNodePool(nodes);
			//var settings = new ElasticsearchClientSettings(connectionPool)
			//	.RequestTimeout(TimeSpan.FromSeconds(5))
			//	.ServerCertificateValidationCallback(CertificateValidations.AllowAll)
			//	.OnRequestCompleted(d =>
			//	{
			//		// ON CI only logged failed requests
			//		// Locally we just log everything for ease of development
			//		try
			//		{
			//			if (isCi)
			//			{
			//				if (!d.HasSuccessfulStatusCode)
			//					output.WriteLine(d.DebugInformation);
			//			}
			//			else output.WriteLine(d.DebugInformation);
			//		}
			//		catch
			//		{
			//			// ignored
			//		}
			//	})
			//	.EnableDebugMode()
			//	//do not request server stack traces on CI, too noisy
			//	.IncludeServerStackTraceOnError(!isCi);
			//if (cluster.DetectedProxy != None)
			//{
			//	var proxyUrl = cluster.DetectedProxy == Fiddler ? "ipv4.fiddler" : "localhost";
			//	settings = settings.Proxy(new Uri($"http://{proxyUrl}:8080"), null!, null!);
			//}

			return new ElasticsearchClient();
		});
}

public class SecurityCluster() : IngestionCluster(new XunitClusterConfiguration(Version, ClusterFeatures.Security)
{
	StartingPortNumber = 9202, TrialMode = XPackTrialMode.Trial
});
