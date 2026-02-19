// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using Elastic.Clients.Elasticsearch;
using Elastic.Elasticsearch.Ephemeral;
using Elastic.TUnit.Elasticsearch.Core;
using Elastic.Transport;
using Microsoft.Extensions.Configuration;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

/// <summary> Declare our cluster that we want to inject into our test classes </summary>
public class IngestionCluster : ElasticsearchCluster<ElasticsearchConfiguration>
{
	protected static readonly string Version = "latest-9";

	public IngestionCluster() : this(new ElasticsearchConfiguration(Version)) { }

	protected IngestionCluster(ElasticsearchConfiguration configuration) : base(configuration) { }

	public ElasticsearchClient Client => this.GetOrAddClient((_, output) =>
	{
		var settings = new ElasticsearchClientSettings(new StaticNodePool(NodesUris()))
			.RequestTimeout(TimeSpan.FromSeconds(5))
			.ServerCertificateValidationCallback(CertificateValidations.AllowAll)
			.EnableDebugMode()
			.IncludeServerStackTraceOnError(false)
			.OnRequestCompleted(d =>
			{
				try { output.WriteLine(d.DebugInformation); }
				catch
				{
					// ignored
				}
			});

		if (ExternalApiKey != null)
			settings = settings.Authentication(new ApiKey(ExternalApiKey));

		return new ElasticsearchClient(settings);
	});

	protected override ExternalClusterConfiguration? TryUseExternalCluster()
	{
		var config = new ConfigurationBuilder()
			.AddUserSecrets(typeof(IngestionCluster).Assembly, optional: true)
			.Build();

		var url = config["Parameters:ElasticsearchUrl"];
		if (string.IsNullOrEmpty(url))
			return null;

		var apiKey = config["Parameters:ElasticsearchApiKey"];
		return new ExternalClusterConfiguration(
			new Uri(url),
			string.IsNullOrWhiteSpace(apiKey) ? null : apiKey);
	}
}

public class SecurityCluster() : IngestionCluster(new ElasticsearchConfiguration(Version, ClusterFeatures.Security)
{
	TrialMode = XPackTrialMode.Trial
});
