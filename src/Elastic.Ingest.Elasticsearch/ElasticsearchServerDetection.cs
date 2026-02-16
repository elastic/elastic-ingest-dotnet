// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch;

internal static class ElasticsearchServerDetection
{
	public static bool IsServerless(ITransport transport)
	{
		var rootInfo = transport.Request<JsonResponse>(HttpMethod.GET, "/");
		if (rootInfo.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception(
				$"Failed to detect server: {rootInfo}",
				rootInfo.ApiCallDetails.OriginalException
			);
		return rootInfo.Get<string>("version.build_flavor") == "serverless";
	}

	public static async Task<bool> IsServerlessAsync(ITransport transport, CancellationToken ctx = default)
	{
		var rootInfo = await transport.RequestAsync<JsonResponse>(HttpMethod.GET, "/", cancellationToken: ctx)
			.ConfigureAwait(false);
		if (rootInfo.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception(
				$"Failed to detect server: {rootInfo}",
				rootInfo.ApiCallDetails.OriginalException
			);
		return rootInfo.Get<string>("version.build_flavor") == "serverless";
	}

	public static int GetShardCount(ITransport transport, string index)
	{
		var response = transport.Request<JsonResponse>(
			HttpMethod.GET,
			$"/{index}/_settings?filter_path=*.settings.index.number_of_shards"
		);
		if (response.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception(
				$"Failed to get shard count for index '{index}': {response}",
				response.ApiCallDetails.OriginalException
			);
		return ParseShardCount(response);
	}

	public static async Task<int> GetShardCountAsync(ITransport transport, string index, CancellationToken ctx = default)
	{
		var response = await transport.RequestAsync<JsonResponse>(
			HttpMethod.GET,
			$"/{index}/_settings?filter_path=*.settings.index.number_of_shards",
			cancellationToken: ctx
		).ConfigureAwait(false);
		if (response.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception(
				$"Failed to get shard count for index '{index}': {response}",
				response.ApiCallDetails.OriginalException
			);
		return ParseShardCount(response);
	}

	private static int ParseShardCount(JsonResponse response)
	{
		if (response.Body is not JsonObject root)
			return 1;

		// Response shape: { "<index_name>": { "settings": { "index": { "number_of_shards": "5" } } } }
		foreach (var property in root)
		{
			var shards = response.Get<string>($"{property.Key}.settings.index.number_of_shards");
			if (shards != null && int.TryParse(shards, out var count))
				return count;
		}
		return 1; // Default fallback
	}
}
