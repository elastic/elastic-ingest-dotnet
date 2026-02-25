// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
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
	/// Deletes lookup entries whose match field value doesn't exist in the target index.
	/// Uses <see cref="PointInTimeSearch{TDocument}"/> over the lookup index, then
	/// batch-checks existence against the target via terms queries.
	/// </summary>
	public async Task CleanupOrphanedAsync(string targetIndex, CancellationToken ct = default)
	{
		var orphanUrls = new List<string>();

		var pit = new PointInTimeSearch<JsonElement>(_transport, new PointInTimeSearchOptions
		{
			Index = _provider.LookupIndexName,
			Size = 1000,
			Slices = 1
		});

		try
		{
			await foreach (var page in pit.SearchPagesAsync(ct).ConfigureAwait(false))
			{
				var urls = new List<string>();
				foreach (var source in page.Documents)
				{
					if (source.TryGetProperty(_provider.MatchField, out var urlProp)
						&& urlProp.GetString() is { } url)
						urls.Add(url);
				}

				if (urls.Count == 0)
					continue;

				var existing = await FindExistingUrlsAsync(targetIndex, urls, ct).ConfigureAwait(false);
				foreach (var url in urls)
				{
					if (!existing.Contains(url))
						orphanUrls.Add(url);
				}
			}
		}
		finally
		{
			await pit.DisposeAsync().ConfigureAwait(false);
		}

		if (orphanUrls.Count > 0)
		{
			var terms = string.Join(",", orphanUrls.Select(u =>
			{
				var encoded = JsonEncodedText.Encode(u);
				return $"\"{encoded}\"";
			}));
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
		var seconds = (long)maxAge.TotalSeconds;
		var query = $"{{\"range\":{{\"created_at\":{{\"lt\":\"now-{seconds}s\"}}}}}}";
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
		var sourceFields = new List<string>(_provider.RequiredSourceFields) { _provider.MatchField };
		foreach (var phField in _provider.FieldPromptHashFieldNames.Values)
			sourceFields.Add(phField);
		var sourceJson = string.Join(",", sourceFields.Select(f => $"\"{f}\""));

		var searchAfterClause = searchAfter != null
			? $",\"search_after\":[{string.Join(",", searchAfter.Select(v => $"\"{v}\""))}]"
			: "";

		var query = $"{{\"size\":{size},\"query\":{BuildStalenessQuery()},\"_source\":[{sourceJson}],\"sort\":[{{\"_doc\":\"asc\"}}]{searchAfterClause}}}";
		var body = await RequestAsync(HttpMethod.POST, $"{index}/_search", query, ct).ConfigureAwait(false);

		if (body == null)
			return (new List<CandidateDocument>(), null);

		return ParseCandidateResponse(body);
	}

	/// <summary>
	/// Builds a bool/should query that matches documents where any enrichment field
	/// is missing or has a stale prompt hash.
	/// </summary>
	private string BuildStalenessQuery()
	{
		var shouldClauses = BuildStalenessShouldClauses();
		return $"{{\"bool\":{{\"should\":[{shouldClauses}],\"minimum_should_match\":1}}}}";
	}

	private string BuildStalenessShouldClauses()
	{
		var clauses = new List<string>();
		foreach (var field in _provider.EnrichmentFields)
		{
			clauses.Add($"{{\"bool\":{{\"must_not\":{{\"exists\":{{\"field\":\"{field}\"}}}}}}}}");

			if (_provider.FieldPromptHashFieldNames.TryGetValue(field, out var phField)
				&& _provider.FieldPromptHashes.TryGetValue(field, out var phValue))
			{
				clauses.Add($"{{\"bool\":{{\"must_not\":{{\"term\":{{\"{phField}\":\"{phValue}\"}}}}}}}}");
			}
		}
		return string.Join(",", clauses);
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
		var encodedPrompt = JsonEncodedText.Encode(prompt);
		var body = $"{{\"input\":\"{encodedPrompt}\"}}";
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
		var now = DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

		foreach (var (urlHash, matchValue, json) in updates)
		{
			sb.Append("{\"update\":{\"_id\":\"").Append(urlHash).Append("\"}}\n");

			sb.Append("{\"doc\":{\"").Append(_provider.MatchField).Append("\":");
			sb.Append('"').Append(JsonEncodedText.Encode(matchValue)).Append("\",");
			sb.Append("\"created_at\":\"").Append(now).Append("\",");

			using var partialDoc = JsonDocument.Parse(json);
			var first = true;
			foreach (var prop in partialDoc.RootElement.EnumerateObject())
			{
				if (!first) sb.Append(',');
				first = false;
				sb.Append('"').Append(prop.Name).Append("\":");
				sb.Append(prop.Value.GetRawText());
			}

			sb.Append("},\"doc_as_upsert\":true}\n");
		}

		await _transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"{_provider.LookupIndexName}/_bulk",
			PostData.String(sb.ToString()), cancellationToken: ct).ConfigureAwait(false);
	}

	// ── Private: backfill ──

	private async Task BackfillAsync(string targetIndex, CancellationToken ct)
	{
		var url = $"/{targetIndex}/_update_by_query?wait_for_completion=false&pipeline={_provider.PipelineName}";

		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST, url, PostData.String($"{{\"query\":{BuildStalenessQuery()}}}"),
			cancellationToken: ct).ConfigureAwait(false);

		var taskId = response.Get<string>("task");
		if (!string.IsNullOrEmpty(taskId))
		{
			await foreach (var _ in ElasticsearchTaskMonitor.PollTaskAsync(
				_transport, taskId, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false))
			{
			}
		}
	}

	// ── Private: orphan detection ──

	/// <summary>
	/// Checks which URLs from the batch exist in the target index by querying the match field.
	/// </summary>
	private async Task<HashSet<string>> FindExistingUrlsAsync(
		string targetIndex, List<string> urls, CancellationToken ct)
	{
		var existing = new HashSet<string>();
		var terms = string.Join(",", urls.Select(u =>
		{
			var encoded = JsonEncodedText.Encode(u);
			return $"\"{encoded}\"";
		}));
		var query = $"{{\"size\":{urls.Count},\"_source\":[\"{_provider.MatchField}\"],\"query\":{{\"terms\":{{\"{_provider.MatchField}\":[{terms}]}}}}}}";

		var body = await RequestAsync(HttpMethod.POST, $"{targetIndex}/_search", query, ct).ConfigureAwait(false);
		if (body == null)
			return existing;

		using var doc = JsonDocument.Parse(body);
		if (doc.RootElement.TryGetProperty("hits", out var hitsObj)
			&& hitsObj.TryGetProperty("hits", out var hitsArr))
		{
			foreach (var hit in hitsArr.EnumerateArray())
			{
				if (hit.TryGetProperty("_source", out var source)
					&& source.TryGetProperty(_provider.MatchField, out var urlProp)
					&& urlProp.GetString() is { } url)
					existing.Add(url);
			}
		}

		return existing;
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
