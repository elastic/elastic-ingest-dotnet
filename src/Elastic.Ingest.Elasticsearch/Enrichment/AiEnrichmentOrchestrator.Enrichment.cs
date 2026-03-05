// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Enrichment;

public partial class AiEnrichmentOrchestrator
{
	private async Task<List<string>> QueryCandidateIdsAsync(
		string index, AiEnrichmentOptions opts, CancellationToken ct)
	{
		var ids = new List<string>();
		object[]? searchAfter = null;

		while (ids.Count < opts.MaxEnrichmentsPerRun)
		{
			var remaining = opts.MaxEnrichmentsPerRun - ids.Count;
			var batchSize = Math.Min(opts.QueryBatchSize, remaining);
			var searchAfterClause = searchAfter != null
				? $",\"search_after\":[{string.Join(",", searchAfter.Select(v => $"\"{v}\""))}]"
				: "";

			var query = $"{{\"size\":{batchSize},\"query\":{_stalenessQuery.GetRawText()},\"_source\":false,\"sort\":[{{\"_doc\":\"asc\"}}]{searchAfterClause}}}";
			var response = await _transport.RequestAsync<JsonResponse>(
				HttpMethod.POST, $"{index}/_search", PostData.String(query), cancellationToken: ct).ConfigureAwait(false);

			if (response.ApiCallDetails.HttpStatusCode is not 200)
				break;

			if (response.Body is not JsonObject root)
				break;

			var hitsArray = root["hits"]?["hits"]?.AsArray();
			if (hitsArray == null || hitsArray.Count == 0)
				break;

			foreach (var hitNode in hitsArray)
			{
				var id = hitNode?["_id"]?.GetValue<string>();
				if (id != null)
					ids.Add(id);

				var sortArray = hitNode?["sort"]?.AsArray();
				if (sortArray != null)
					searchAfter = sortArray.Select(e => (object)(e?.ToString() ?? "")).ToArray();
			}

			if (hitsArray.Count < batchSize)
				break;
		}

		return ids;
	}

	private async Task<EsqlChunkResult> RunEsqlCompletionAsync(
		string targetIndex, List<string> docIds, AiEnrichmentOptions opts, CancellationToken ct)
	{
		var updates = new List<LookupUpdate>();
		var enrichedDocIds = new List<string>();

		var idPlaceholders = string.Join(", ", docIds.Select((_, i) => $"?id_{i}"));
		var inferenceId = opts.InferenceEndpointId;
		var esqlQuery = $"FROM {targetIndex} METADATA _id"
			+ $" | WHERE _id IN ({idPlaceholders})"
			+ $" | EVAL prompt = {_provider.EsqlPromptExpression}"
			+ $" | COMPLETION result = prompt WITH {{\"inference_id\": \"{inferenceId}\"}}"
			+ $" | KEEP _id, {_infra.MatchField}, result";

		var body = BuildEsqlRequestBody(esqlQuery, docIds);

		var requestConfig = new RequestConfiguration { RequestTimeout = opts.CompletionTimeout };
		var response = await _transport.RequestAsync<StringResponse>(
			HttpMethod.POST, "_query", PostData.String(body), requestConfig, ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
		{
			var status = response.ApiCallDetails.HttpStatusCode;
			var retryable = status is 408 or 429 or >= 500;
			var errorBody = Truncate(response.Body, 1000);
			return new EsqlChunkResult(updates, enrichedDocIds, docIds.Count,
				$"ES|QL COMPLETION failed (HTTP {status}): {errorBody}", retryable);
		}

		return ParseEsqlResponse(response.Body, docIds, updates, enrichedDocIds);
	}

	private string BuildEsqlRequestBody(string esqlQuery, List<string> docIds)
	{
		var sb = new StringBuilder();
		sb.Append("{\"query\":\"").Append(JsonEncodedText.Encode(esqlQuery)).Append('"');

		var hasParams = _provider.EsqlPromptParams.Count > 0 || docIds.Count > 0;
		if (hasParams)
		{
			sb.Append(",\"params\":[");
			var first = true;
			foreach (var kv in _provider.EsqlPromptParams)
			{
				if (!first) sb.Append(',');
				first = false;
				sb.Append("{\"").Append(JsonEncodedText.Encode(kv.Key))
				  .Append("\":\"").Append(JsonEncodedText.Encode(kv.Value)).Append("\"}");
			}
			for (var i = 0; i < docIds.Count; i++)
			{
				if (!first) sb.Append(',');
				first = false;
				sb.Append("{\"id_").Append(i).Append("\":\"")
				  .Append(JsonEncodedText.Encode(docIds[i])).Append("\"}");
			}
			sb.Append(']');
		}
		sb.Append('}');
		return sb.ToString();
	}

	private EsqlChunkResult ParseEsqlResponse(
		string responseBody, List<string> docIds,
		List<LookupUpdate> updates, List<string> enrichedDocIds)
	{
		JsonNode? rootNode;
		try { rootNode = JsonNode.Parse(responseBody); }
		catch { return new EsqlChunkResult(updates, enrichedDocIds, docIds.Count, "ES|QL response was not valid JSON"); }

		if (rootNode is not JsonObject root)
			return new EsqlChunkResult(updates, enrichedDocIds, docIds.Count, "ES|QL response root was not a JSON object");

		var columns = root["columns"]?.AsArray();
		var values = root["values"]?.AsArray();
		if (columns == null || values == null)
			return new EsqlChunkResult(updates, enrichedDocIds, docIds.Count,
				$"ES|QL response missing columns/values: {Truncate(responseBody, 500)}");

		int idCol = -1, matchCol = -1, resultCol = -1;
		for (var i = 0; i < columns.Count; i++)
		{
			var name = columns[i]?["name"]?.GetValue<string>();
			if (name == "_id") idCol = i;
			else if (name == _infra.MatchField) matchCol = i;
			else if (name == "result") resultCol = i;
		}

		if (idCol < 0 || matchCol < 0 || resultCol < 0)
		{
			var colNames = string.Join(", ", columns.Select(c => c?["name"]?.GetValue<string>() ?? "?"));
			return new EsqlChunkResult(updates, enrichedDocIds, docIds.Count,
				$"Expected columns [_id, {_infra.MatchField}, result] but got [{colNames}]");
		}

		var chunkFailed = 0;
		var allFields = (IReadOnlyCollection<string>)_provider.EnrichmentFields;

		foreach (var row in values)
		{
			if (row is not JsonArray rowArr)
			{
				chunkFailed++;
				continue;
			}

			var docId = rowArr[idCol]?.GetValue<string>();
			var matchValue = rowArr[matchCol]?.GetValue<string>();
			var resultText = rowArr[resultCol]?.GetValue<string>();

			if (matchValue == null || resultText == null)
			{
				chunkFailed++;
				continue;
			}

			var partialDoc = _provider.ParseResponse(resultText, allFields);
			if (partialDoc == null)
			{
				chunkFailed++;
				continue;
			}

			var urlHash = UrlHash(matchValue);
			var lookupDoc = BuildLookupDocument(matchValue, partialDoc);
			updates.Add(new LookupUpdate(urlHash, lookupDoc));
			if (docId != null)
				enrichedDocIds.Add(docId);
		}

		chunkFailed += docIds.Count - (updates.Count + chunkFailed);
		return new EsqlChunkResult(updates, enrichedDocIds, chunkFailed, null);
	}

	private async Task SchedulePendingAfterDelayAsync(
		string targetIndex, AiEnrichmentOptions opts, TimeSpan delay,
		Queue<ChunkMeta> pendingChunks, Dictionary<Task<EsqlChunkResult>, ChunkMeta> activeTasks,
		CancellationToken ct)
	{
		await Task.Delay(delay, ct).ConfigureAwait(false);
		while (pendingChunks.Count > 0 && activeTasks.Count < opts.EsqlConcurrency)
		{
			var next = pendingChunks.Dequeue();
			var task = RunEsqlCompletionAsync(targetIndex, next.DocIds, opts, ct);
			activeTasks[task] = next;
		}
	}
}
