// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.Helpers;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Enrichment;

public partial class AiEnrichmentOrchestrator
{
	// Elasticsearch ids query uses terms on _id under the hood (default
	// index.max_terms_count = 65 536).  We batch well under that to keep
	// individual _update_by_query requests small and predictable.
	private const int BackfillBatchSize = 1_000;

	private JsonElement BuildLookupDocument(string matchValue, string partialDocJson)
	{
		var now = DateTimeOffset.UtcNow.UtcDateTime.ToString(
			"yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

		var sb = new StringBuilder();
		sb.Append('{');
		sb.Append('"').Append(_infra.MatchField).Append("\":");
		sb.Append('"').Append(JsonEncodedText.Encode(matchValue)).Append("\",");
		sb.Append("\"created_at\":\"").Append(now).Append('"');

		using var partialDoc = JsonDocument.Parse(partialDocJson);
		foreach (var prop in partialDoc.RootElement.EnumerateObject())
		{
			sb.Append(',');
			sb.Append('"').Append(prop.Name).Append("\":");
			sb.Append(prop.Value.GetRawText());
		}

		sb.Append('}');
		return JsonDocument.Parse(sb.ToString()).RootElement.Clone();
	}

	private async Task<int> BulkUpsertLookupAsync(List<LookupUpdate> updates, CancellationToken ct)
	{
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
		var items = updates.ToArray();
		var bytes = BulkRequestDataFactory.GetBytes(
			items.AsSpan(),
			IngestChannelStatics.SerializerOptions,
			static u => new UpdateOperation { Id = u.UrlHash },
			static u => u.Document);
		var body = PostData.ReadOnlyMemory(bytes);
#else
		var sb = new StringBuilder();
		foreach (var update in updates)
		{
			sb.Append("{\"update\":{\"_id\":\"").Append(update.UrlHash).Append("\"}}\n");
			sb.Append("{\"doc_as_upsert\":true,\"doc\":").Append(update.Document.GetRawText()).Append("}\n");
		}
		var body = PostData.Bytes(Encoding.UTF8.GetBytes(sb.ToString()));
#endif

		var response = await _transport.RequestAsync<StringResponse>(
			HttpMethod.POST,
			$"{_infra.LookupIndexName}/_bulk?filter_path=errors,items.*.status,items.*.error",
			body, cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			return updates.Count;

		using var doc = JsonDocument.Parse(response.Body);
		if (!doc.RootElement.TryGetProperty("items", out var itemsArray))
			return 0;

		var errors = 0;
		foreach (var item in itemsArray.EnumerateArray())
		{
			foreach (var action in item.EnumerateObject())
			{
				if (action.Value.TryGetProperty("status", out var statusProp))
				{
					var status = statusProp.GetInt32();
					if (status < 200 || status > 299)
						errors++;
				}
			}
		}
		return errors;
	}

	private async Task RefreshAsync(string index, CancellationToken ct) =>
		await _transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"{index}/_refresh", PostData.Empty, cancellationToken: ct).ConfigureAwait(false);

	private async Task BackfillAsync(string targetIndex, List<string> enrichedDocIds, CancellationToken ct)
	{
		if (enrichedDocIds.Count == 0)
			return;

		var batches = ChunkList(enrichedDocIds, BackfillBatchSize);
		foreach (var batch in batches)
		{
			var idsJson = string.Join(",", batch.Select(id => $"\"{JsonEncodedText.Encode(id)}\""));
			var query = JsonDocument.Parse($"{{\"ids\":{{\"values\":[{idsJson}]}}}}").RootElement.Clone();

			await foreach (var _ in RunUpdateByQueryWithPollingAsync(targetIndex, query, ct).ConfigureAwait(false))
			{
			}
		}
	}

	private async IAsyncEnumerable<JsonResponse> RunUpdateByQueryWithPollingAsync(
		string targetIndex, JsonElement query,
		[EnumeratorCancellation] CancellationToken ct,
		TimeSpan? pollInterval = null)
	{
		var interval = pollInterval ?? TimeSpan.FromSeconds(5);
		var url = $"/{targetIndex}/_update_by_query?wait_for_completion=false&pipeline={_infra.PipelineName}";

		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST, url,
			PostData.Serializable(new QueryRequest { Query = query }),
			cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			yield break;

		var taskId = response.Get<string>("task");
		if (string.IsNullOrEmpty(taskId))
			yield break;

		await foreach (var taskResponse in ElasticsearchTaskMonitor.PollTaskAsync(
			_transport, taskId, interval, ct).ConfigureAwait(false))
		{
			yield return taskResponse;
		}
	}
}
