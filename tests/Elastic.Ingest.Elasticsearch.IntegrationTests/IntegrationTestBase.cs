// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

public abstract class IntegrationTestBase(IngestionCluster cluster) : IntegrationTestBase<IngestionCluster>(cluster);

public abstract class IntegrationTestBase<TCluster>(TCluster cluster)
	where TCluster : IngestionCluster
{
	protected TCluster Cluster { get; } = cluster;
	protected ElasticsearchClient Client => Cluster.Client;

	/// <summary>
	/// A <see cref="DistributedTransport"/> built directly from <see cref="TransportConfigurationDescriptor"/>
	/// using Elastic.Transport 0.12.0+. All tests should use this instead of <c>Client.Transport</c>.
	/// <para>
	/// Elastic.Clients.Elasticsearch 9.2.1 was compiled against Elastic.Transport 0.10.x which lacks
	/// the <c>SocketsHttpHandler</c> connection-pool fixes and single-node stale-connection retry
	/// added in 0.12.0. Using <c>Client.Transport</c> would still run the old code paths at runtime
	/// because the client's <c>HttpRequestInvoker</c> was instantiated from the older assembly surface.
	/// </para>
	/// <para>
	/// This transport also bypasses the ES client's <c>ElasticsearchResponseBuilder</c> so that
	/// <c>JsonResponse</c>, <c>StringResponse</c> etc. are handled by the built-in response builders.
	/// </para>
	/// <para>
	/// TODO: Remove once Elastic.Clients.Elasticsearch depends on Elastic.Transport &gt;= 0.12.0.
	/// At that point <c>Client.Transport</c> can be used directly.
	/// </para>
	/// </summary>
	protected ITransport Transport
	{
		get
		{
			if (_transport is not null)
				return _transport;

			var config = new TransportConfigurationDescriptor(new StaticNodePool(Cluster.NodesUris()))
				.DisableDirectStreaming()
				.ServerCertificateValidationCallback(CertificateValidations.AllowAll)
				.RequestTimeout(TimeSpan.FromSeconds(30));

			if (Cluster.ExternalApiKey is not null)
				config.Authentication(new ApiKey(Cluster.ExternalApiKey));

			_transport = new DistributedTransport(config);
			return _transport;
		}
	}
	private ITransport? _transport;

	/// <summary>
	/// Deletes indices, data streams, index templates, and component templates
	/// matching the given prefix. Safe for serverless clusters (avoids wildcard
	/// index deletion by resolving concrete index names first).
	/// </summary>
	protected async Task CleanupPrefixAsync(string prefix)
	{
		var transport = Transport;

		await transport.RequestAsync<StringResponse>(
			HttpMethod.DELETE, $"/_data_stream/{prefix}-*", cancellationToken: default
		).ConfigureAwait(false);

		// Delete the exact-name index (e.g. "reindex-dst") which the wildcard pattern won't match
		await transport.RequestAsync<StringResponse>(
			HttpMethod.DELETE, $"/{prefix}?ignore_unavailable=true", cancellationToken: default
		).ConfigureAwait(false);

		await DeleteConcreteIndicesAsync(transport, $"{prefix}-*");

		await transport.RequestAsync<StringResponse>(
			HttpMethod.DELETE, $"/_index_template/{prefix}*",
			cancellationToken: default
		).ConfigureAwait(false);

		await transport.RequestAsync<StringResponse>(
			HttpMethod.DELETE, $"/_component_template/{prefix}*",
			cancellationToken: default
		).ConfigureAwait(false);
	}

	private static async Task DeleteConcreteIndicesAsync(ITransport transport, string pattern)
	{
		var resolve = await transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_resolve/index/{pattern}",
			cancellationToken: default
		).ConfigureAwait(false);

		if (!resolve.ApiCallDetails.HasSuccessfulStatusCode || string.IsNullOrEmpty(resolve.Body))
			return;

		try
		{
			using var doc = JsonDocument.Parse(resolve.Body);
			var indices = doc.RootElement
				.GetProperty("indices")
				.EnumerateArray()
				.Select(e => e.GetProperty("name").GetString())
				.Where(n => !string.IsNullOrEmpty(n))
				.ToList();

			foreach (var index in indices)
			{
				await transport.RequestAsync<StringResponse>(
					HttpMethod.DELETE, $"/{index}?ignore_unavailable=true",
					cancellationToken: default
				).ConfigureAwait(false);
			}
		}
		catch
		{
			// Resolve API may not be available; fall back to best effort
		}
	}
}
