// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.Helpers;
using Elastic.Mapping;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Enrichment;

/// <summary>
/// Post-indexing AI enrichment orchestrator. Manages the full lifecycle:
/// <list type="bullet">
///   <item><see cref="InitializeAsync"/> — create lookup index, enrich policy, and pipeline before indexing</item>
///   <item><see cref="EnrichAsync"/> — query for unenriched/stale docs, call LLM per-field, update lookup, backfill</item>
///   <item><see cref="CleanupOrphanedAsync"/>, <see cref="CleanupOlderThanAsync"/>, <see cref="PurgeAsync"/> — manage stale cache entries</item>
/// </list>
/// <para>
/// No in-memory cache — the lookup index is the persistent store and the target index
/// is queried directly to find candidates. Per-field prompt hashing ensures that changing
/// one field's prompt only regenerates that field.
/// </para>
/// </summary>
public class AiEnrichmentOrchestrator : IDisposable
{
	private readonly ITransport _transport;
	private readonly IAiEnrichmentProvider _provider;
	private readonly AiEnrichmentOptions _options;
	private readonly SemaphoreSlim _throttle;

	/// <inheritdoc cref="AiEnrichmentOrchestrator"/>
	public AiEnrichmentOrchestrator(
		ITransport transport,
		IAiEnrichmentProvider provider,
		AiEnrichmentOptions? options = null)
	{
		_transport = transport ?? throw new ArgumentNullException(nameof(transport));
		_provider = provider ?? throw new ArgumentNullException(nameof(provider));
		_options = options ?? new AiEnrichmentOptions();
		_throttle = new SemaphoreSlim(_options.MaxConcurrency);
	}

	/// <summary>
	/// Pre-bootstrap: ensures the lookup index, enrich policy, and ingest pipeline exist.
	/// Call this before indexing starts (e.g. via <c>AddPreBootstrapTask</c>).
	/// </summary>
	public async Task InitializeAsync(CancellationToken ct = default)
	{
		await EnsureLookupIndexAsync(ct).ConfigureAwait(false);
		await EnsureEnrichPolicyAsync(ct).ConfigureAwait(false);
		await ExecuteEnrichPolicyAsync(ct).ConfigureAwait(false);
		await EnsurePipelineAsync(ct).ConfigureAwait(false);
	}

	/// <summary>
	/// Post-indexing: queries the target index for documents needing enrichment,
	/// calls the LLM for stale/missing fields, stores results in the lookup index,
	/// then re-executes the enrich policy and backfills.
	/// </summary>
	public async Task<AiEnrichmentResult> EnrichAsync(string targetIndex, CancellationToken ct = default)
	{
		var enriched = 0;
		var failed = 0;
		var totalCandidates = 0;
		object[]? searchAfter = null;

		while (enriched + failed < _options.MaxEnrichmentsPerRun)
		{
			var remaining = _options.MaxEnrichmentsPerRun - enriched - failed;
			var batchSize = Math.Min(_options.QueryBatchSize, remaining);

			var (candidates, nextSearchAfter) = await QueryCandidatesAsync(
				targetIndex, batchSize, searchAfter, ct).ConfigureAwait(false);

			if (candidates.Count == 0)
				break;

			totalCandidates += candidates.Count;
			searchAfter = nextSearchAfter;

			var (batchEnriched, batchFailed) = await ProcessBatchAsync(candidates, ct).ConfigureAwait(false);

			enriched += batchEnriched;
			failed += batchFailed;
		}

		if (enriched > 0)
		{
			await ExecuteEnrichPolicyAsync(ct).ConfigureAwait(false);
			await BackfillAsync(targetIndex, ct).ConfigureAwait(false);
		}

		return new AiEnrichmentResult
		{
			TotalCandidates = totalCandidates,
			Enriched = enriched,
			Failed = failed,
			ReachedLimit = enriched + failed >= _options.MaxEnrichmentsPerRun
		};
	}

	/// <summary>
	/// Deletes lookup entries whose URL doesn't exist in the target index.
	/// </summary>
	public async Task CleanupOrphanedAsync(string targetIndex, CancellationToken ct = default)
	{
		var orphanUrls = new List<string>();
		string? scrollId = null;

		// Scroll lookup index for all URLs
		var scrollQuery = $"{{\"size\":1000,\"_source\":[\"{_provider.MatchField}\"],\"query\":{{\"match_all\":{{}}}}}}";
		var response = await RequestAsync(HttpMethod.POST,
			$"{_provider.LookupIndexName}/_search?scroll=1m",
			scrollQuery, ct).ConfigureAwait(false);

		while (response != null)
		{
			using var doc = JsonDocument.Parse(response);
			scrollId = doc.RootElement.TryGetProperty("_scroll_id", out var sid)
				? sid.GetString() : null;

			if (!doc.RootElement.TryGetProperty("hits", out var hitsObj)
				|| !hitsObj.TryGetProperty("hits", out var hitsArr)
				|| hitsArr.GetArrayLength() == 0)
				break;

			// Collect URLs from this page
			var urls = new List<string>();
			foreach (var hit in hitsArr.EnumerateArray())
			{
				if (hit.TryGetProperty("_source", out var source)
					&& source.TryGetProperty(_provider.MatchField, out var urlProp))
				{
					var url = urlProp.GetString();
					if (url != null) urls.Add(url);
				}
			}

			// Batch _mget against target to check existence
			if (urls.Count > 0)
			{
				var mgetDocs = string.Join(",", urls.Select(u => $"{{\"_id\":\"{UrlHash(u)}\"}}"));
				var mgetBody = $"{{\"docs\":[{mgetDocs}]}}";
				var mgetResponse = await RequestAsync(HttpMethod.POST,
					$"{targetIndex}/_mget?_source=false", mgetBody, ct).ConfigureAwait(false);

				if (mgetResponse != null)
				{
					using var mgetDoc = JsonDocument.Parse(mgetResponse);
					if (mgetDoc.RootElement.TryGetProperty("docs", out var docs))
					{
						var i = 0;
						foreach (var d in docs.EnumerateArray())
						{
							if (i < urls.Count && (!d.TryGetProperty("found", out var found) || !found.GetBoolean()))
								orphanUrls.Add(urls[i]);
							i++;
						}
					}
				}
			}

			// Next scroll page
			if (scrollId == null) break;
			var scrollBody = $"{{\"scroll\":\"1m\",\"scroll_id\":\"{scrollId}\"}}";
			response = await RequestAsync(HttpMethod.POST, "_search/scroll", scrollBody, ct).ConfigureAwait(false);
		}

		// Delete orphaned entries
		if (orphanUrls.Count > 0)
		{
			var terms = string.Join(",", orphanUrls.Select(u => $"\"{u}\""));
			var deleteQuery = $"{{\"terms\":{{\"{_provider.MatchField}\":[{terms}]}}}}";
			var dbq = new DeleteByQuery(_transport, new DeleteByQueryOptions
			{
				Index = _provider.LookupIndexName,
				QueryBody = deleteQuery
			});
			await dbq.RunAsync(ct).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Deletes lookup entries older than the specified age.
	/// </summary>
	public async Task CleanupOlderThanAsync(TimeSpan maxAge, CancellationToken ct = default)
	{
		var millis = (long)maxAge.TotalMilliseconds;
		var query = $"{{\"range\":{{\"created_at\":{{\"lt\":\"now-{millis}ms\"}}}}}}";
		var dbq = new DeleteByQuery(_transport, new DeleteByQueryOptions
		{
			Index = _provider.LookupIndexName,
			QueryBody = query
		});
		await dbq.RunAsync(ct).ConfigureAwait(false);
	}

	/// <summary>
	/// Purges all entries from the lookup index.
	/// </summary>
	public async Task PurgeAsync(CancellationToken ct = default)
	{
		var dbq = new DeleteByQuery(_transport, new DeleteByQueryOptions
		{
			Index = _provider.LookupIndexName,
			QueryBody = "{\"match_all\":{}}"
		});
		await dbq.RunAsync(ct).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_throttle.Dispose();
		GC.SuppressFinalize(this);
	}

	// ── Private: initialization ──

	private async Task EnsureLookupIndexAsync(CancellationToken ct)
	{
		var exists = await _transport.RequestAsync<StringResponse>(
			HttpMethod.HEAD, _provider.LookupIndexName, cancellationToken: ct).ConfigureAwait(false);

		if (exists.ApiCallDetails.HttpStatusCode == 200)
			return;

		await _transport.RequestAsync<StringResponse>(
			HttpMethod.PUT, _provider.LookupIndexName,
			PostData.String(_provider.LookupIndexMapping), cancellationToken: ct).ConfigureAwait(false);
	}

	private async Task EnsureEnrichPolicyAsync(CancellationToken ct)
	{
		var exists = await _transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"_enrich/policy/{_provider.EnrichPolicyName}",
			cancellationToken: ct).ConfigureAwait(false);

		if (exists.ApiCallDetails.HttpStatusCode == 200
			&& exists.Body?.Contains(_provider.EnrichPolicyName) == true)
			return;

		await _transport.RequestAsync<StringResponse>(
			HttpMethod.PUT, $"_enrich/policy/{_provider.EnrichPolicyName}",
			PostData.String(_provider.EnrichPolicyBody), cancellationToken: ct).ConfigureAwait(false);
	}

	private async Task ExecuteEnrichPolicyAsync(CancellationToken ct)
	{
		await _transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"_enrich/policy/{_provider.EnrichPolicyName}/_execute",
			PostData.Empty, cancellationToken: ct).ConfigureAwait(false);
	}

	private async Task EnsurePipelineAsync(CancellationToken ct)
	{
		await _transport.RequestAsync<StringResponse>(
			HttpMethod.PUT, $"_ingest/pipeline/{_provider.PipelineName}",
			PostData.String(_provider.PipelineBody), cancellationToken: ct).ConfigureAwait(false);
	}

	// ── Private: candidate querying ──

	private async Task<(List<CandidateDocument> Hits, object[]? SearchAfter)> QueryCandidatesAsync(
		string index, int size, object[]? searchAfter, CancellationToken ct)
	{
		var query = BuildCandidateQuery(size, searchAfter);
		var body = await RequestAsync(HttpMethod.POST, $"{index}/_search", query, ct).ConfigureAwait(false);

		if (body == null)
			return (new List<CandidateDocument>(), null);

		return ParseCandidateResponse(body);
	}

	private string BuildCandidateQuery(int size, object[]? searchAfter)
	{
		var shouldClauses = new List<string>();

		foreach (var field in _provider.EnrichmentFields)
		{
			// Field missing
			shouldClauses.Add($"{{\"bool\":{{\"must_not\":{{\"exists\":{{\"field\":\"{field}\"}}}}}}}}");

			// Prompt hash stale
			if (_provider.FieldPromptHashFieldNames.TryGetValue(field, out var phField)
				&& _provider.FieldPromptHashes.TryGetValue(field, out var phValue))
			{
				shouldClauses.Add($"{{\"bool\":{{\"must_not\":{{\"term\":{{\"{phField}\":\"{phValue}\"}}}}}}}}");
			}
		}

		// Source: inputs for prompt building + per-field prompt hashes for staleness detection
		var sourceFields = new List<string>(_provider.RequiredSourceFields);
		sourceFields.Add(_provider.MatchField);
		foreach (var phField in _provider.FieldPromptHashFieldNames.Values)
			sourceFields.Add(phField);
		var sourceJson = string.Join(",", sourceFields.Select(f => $"\"{f}\""));

		var searchAfterClause = searchAfter != null
			? $",\"search_after\":[{string.Join(",", searchAfter.Select(v => $"\"{v}\""))}]"
			: "";

		return $"{{\"size\":{size},\"query\":{{\"bool\":{{\"should\":[{string.Join(",", shouldClauses)}],\"minimum_should_match\":1}}}},\"_source\":[{sourceJson}],\"sort\":[{{\"_doc\":\"asc\"}}]{searchAfterClause}}}";
	}

	private static (List<CandidateDocument>, object[]?) ParseCandidateResponse(string body)
	{
		using var doc = JsonDocument.Parse(body);
		var hits = doc.RootElement.GetProperty("hits").GetProperty("hits");
		var candidates = new List<CandidateDocument>();
		object[]? lastSort = null;

		foreach (var hit in hits.EnumerateArray())
		{
			var id = hit.GetProperty("_id").GetString()!;
			var source = hit.GetProperty("_source");
			candidates.Add(new CandidateDocument(id, source.Clone()));

			if (hit.TryGetProperty("sort", out var sort))
				lastSort = sort.EnumerateArray().Select(e => (object)e.ToString()).ToArray();
		}

		return (candidates, lastSort);
	}

	// ── Private: enrichment processing ──

	private async Task<(int Enriched, int Failed)> ProcessBatchAsync(
		List<CandidateDocument> candidates, CancellationToken ct)
	{
		var tasks = candidates.Select(c => EnrichOneAsync(c, ct)).ToList();
		var pendingUpdates = new List<(string UrlHash, string MatchValue, string Json)>();
		var batchFailed = 0;

#if NET9_0_OR_GREATER
		await foreach (var completedTask in Task.WhenEach(tasks).WithCancellation(ct).ConfigureAwait(false))
		{
			var (urlHash, matchValue, json) = await completedTask.ConfigureAwait(false);
			if (json != null && matchValue != null)
				pendingUpdates.Add((urlHash, matchValue, json));
			else
				batchFailed++;
		}
#else
		var pending = new HashSet<Task<(string UrlHash, string? MatchValue, string? Json)>>(tasks);
		while (pending.Count > 0)
		{
			ct.ThrowIfCancellationRequested();
			var completed = await Task.WhenAny(pending).ConfigureAwait(false);
			pending.Remove(completed);
			var (urlHash, matchValue, json) = await completed.ConfigureAwait(false);
			if (json != null && matchValue != null)
				pendingUpdates.Add((urlHash, matchValue, json));
			else
				batchFailed++;
		}
#endif

		if (pendingUpdates.Count > 0)
			await BulkUpsertLookupAsync(pendingUpdates, ct).ConfigureAwait(false);

		return (pendingUpdates.Count, batchFailed);
	}

	private async Task<(string UrlHash, string? MatchValue, string? Json)> EnrichOneAsync(
		CandidateDocument candidate, CancellationToken ct)
	{
		await _throttle.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			// Determine which fields are stale for this document
			var staleFields = DetermineStaleFields(candidate.Source);
			if (staleFields.Count == 0)
				return (candidate.Id, null, null);

			var prompt = _provider.BuildPrompt(candidate.Source, staleFields);
			if (prompt == null)
				return (candidate.Id, null, null);

			var completion = await CallCompletionAsync(prompt, ct).ConfigureAwait(false);
			if (completion == null)
				return (candidate.Id, null, null);

			var partialDoc = _provider.ParseResponse(completion, staleFields);

			// Extract the match field value for the lookup doc ID
			string? matchValue = null;
			if (candidate.Source.TryGetProperty(_provider.MatchField, out var mv))
				matchValue = mv.GetString();

			var urlHash = matchValue != null ? UrlHash(matchValue) : candidate.Id;
			return (urlHash, matchValue, partialDoc);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			return (candidate.Id, null, null);
		}
		finally
		{
			_throttle.Release();
		}
	}

	private List<string> DetermineStaleFields(JsonElement source)
	{
		var stale = new List<string>();
		foreach (var field in _provider.EnrichmentFields)
		{
			if (!_provider.FieldPromptHashFieldNames.TryGetValue(field, out var phField)
				|| !_provider.FieldPromptHashes.TryGetValue(field, out var currentHash))
			{
				stale.Add(field);
				continue;
			}

			// Missing prompt hash field or mismatched value → stale
			if (!source.TryGetProperty(phField, out var existingHash)
				|| existingHash.ValueKind != JsonValueKind.String
				|| existingHash.GetString() != currentHash)
			{
				stale.Add(field);
			}
		}
		return stale;
	}

	private async Task<string?> CallCompletionAsync(string prompt, CancellationToken ct)
	{
		using var buffer = new MemoryStream();
		using (var writer = new Utf8JsonWriter(buffer))
		{
			writer.WriteStartObject();
			writer.WriteString("input", prompt);
			writer.WriteEndObject();
		}
		var body = Encoding.UTF8.GetString(buffer.ToArray());
		var url = $"_inference/completion/{_options.InferenceEndpointId}";

		var response = await _transport.RequestAsync<StringResponse>(
			HttpMethod.POST, url, PostData.String(body), cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			return null;

		return ExtractCompletionText(response.Body);
	}

	private static string? ExtractCompletionText(string? responseBody)
	{
		if (string.IsNullOrEmpty(responseBody))
			return null;

		using var doc = JsonDocument.Parse(responseBody!);
		if (doc.RootElement.TryGetProperty("completion", out var arr)
			&& arr.GetArrayLength() > 0
			&& arr[0].TryGetProperty("result", out var result))
			return result.GetString();

		return null;
	}

	// ── Private: lookup index updates ──

	private async Task BulkUpsertLookupAsync(
		List<(string UrlHash, string MatchValue, string Json)> updates, CancellationToken ct)
	{
		var sb = new StringBuilder();
		foreach (var (urlHash, matchValue, json) in updates)
		{
			sb.Append("{\"update\":{\"_id\":\"").Append(urlHash).Append("\"}}\n");
			var encodedMatch = JsonEncodedText.Encode(matchValue);
			sb.Append("{\"doc\":{\"").Append(_provider.MatchField).Append("\":\"")
				.Append(encodedMatch).Append("\",\"created_at\":\"")
				.Append(DateTimeOffset.UtcNow.ToString("o")).Append("\",")
				.Append(json, 1, json.Length - 1) // strip leading '{', keep rest
				.Append(",\"doc_as_upsert\":true}\n");
		}

		await _transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"{_provider.LookupIndexName}/_bulk",
			PostData.String(sb.ToString()), cancellationToken: ct).ConfigureAwait(false);
	}

	// ── Private: backfill ──

	private async Task BackfillAsync(string targetIndex, CancellationToken ct)
	{
		var shouldClauses = new List<string>();
		foreach (var field in _provider.EnrichmentFields)
		{
			shouldClauses.Add($"{{\"bool\":{{\"must_not\":{{\"exists\":{{\"field\":\"{field}\"}}}}}}}}");

			if (_provider.FieldPromptHashFieldNames.TryGetValue(field, out var phField)
				&& _provider.FieldPromptHashes.TryGetValue(field, out var phValue))
			{
				shouldClauses.Add($"{{\"bool\":{{\"must_not\":{{\"term\":{{\"{phField}\":\"{phValue}\"}}}}}}}}");
			}
		}

		var query = $"{{\"bool\":{{\"should\":[{string.Join(",", shouldClauses)}],\"minimum_should_match\":1}}}}";
		var url = $"/{targetIndex}/_update_by_query?wait_for_completion=false&pipeline={_provider.PipelineName}";

		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST, url, PostData.String($"{{\"query\":{query}}}"),
			cancellationToken: ct).ConfigureAwait(false);

		var taskId = response.Get<string>("task");
		if (!string.IsNullOrEmpty(taskId))
		{
			await foreach (var _ in ElasticsearchTaskMonitor.PollTaskAsync(
				_transport, taskId, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false))
			{
				// Just wait for completion
			}
		}
	}

	// ── Private: helpers ──

	private async Task<string?> RequestAsync(HttpMethod method, string path, string body, CancellationToken ct)
	{
		var response = await _transport.RequestAsync<StringResponse>(
			method, path, PostData.String(body), cancellationToken: ct).ConfigureAwait(false);

		return response.ApiCallDetails.HttpStatusCode is 200 or 201
			? response.Body
			: null;
	}

	internal static string UrlHash(string url)
	{
#if NET8_0_OR_GREATER
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
		return Convert.ToHexString(hash).ToLowerInvariant();
#else
		using var sha = SHA256.Create();
		var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
		return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
#endif
	}

}

internal sealed class CandidateDocument
{
	public CandidateDocument(string id, JsonElement source)
	{
		Id = id;
		Source = source;
	}

	public string Id { get; }
	public JsonElement Source { get; }
}
