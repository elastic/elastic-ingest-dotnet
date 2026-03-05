// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.Helpers;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Enrichment;

public partial class AiEnrichmentOrchestrator
{
	private async Task<HashSet<string>> FindExistingUrlsAsync(
		string targetIndex, List<string> urls, CancellationToken ct)
	{
		var existing = new HashSet<string>();
		var terms = string.Join(",", urls.Select(u =>
		{
			var encoded = JsonEncodedText.Encode(u);
			return $"\"{encoded}\"";
		}));

		var query = $"{{\"size\":{urls.Count}"
			+ $",\"_source\":[\"{_infra.MatchField}\"]"
			+ $",\"query\":{{\"terms\":{{\"{_infra.MatchField}\":[{terms}]}}}}"
			+ $",\"collapse\":{{\"field\":\"{_infra.MatchField}\"}}"
			+ "}";

		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST, $"{targetIndex}/_search", PostData.String(query), cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			return existing;

		if (response.Body is not JsonObject root)
			return existing;

		var hitsArray = root["hits"]?["hits"]?.AsArray();
		if (hitsArray == null)
			return existing;

		foreach (var hitNode in hitsArray)
		{
			var url = hitNode?["_source"]?[_infra.MatchField]?.GetValue<string>();
			if (url != null)
				existing.Add(url);
		}

		return existing;
	}

	private async Task DeleteByMatchFieldAsync(string index, List<string> values, CancellationToken ct)
	{
		var terms = string.Join(",", values.Select(u =>
		{
			var encoded = JsonEncodedText.Encode(u);
			return $"\"{encoded}\"";
		}));
		var deleteQuery = $"{{\"terms\":{{\"{_infra.MatchField}\":[{terms}]}}}}";
		var dbq = new DeleteByQuery(_transport, new DeleteByQueryOptions
		{
			Index = index,
			QueryBody = deleteQuery
		});
		await dbq.RunAsync(ct).ConfigureAwait(false);
	}
}
