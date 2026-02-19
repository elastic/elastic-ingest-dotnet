// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

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
	/// Deletes indices, data streams, index templates, and component templates
	/// matching the given prefix. Safe for serverless clusters (avoids wildcard
	/// index deletion by resolving concrete index names first).
	/// </summary>
	protected async Task CleanupPrefixAsync(string prefix)
	{
		var transport = Client.Transport;

		await transport.RequestAsync<StringResponse>(
			HttpMethod.DELETE, $"/_data_stream/{prefix}-*", cancellationToken: default
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
