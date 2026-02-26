// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.Helpers;
using Elastic.Ingest.Elasticsearch.Serialization;
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
/// </summary>
public class AiEnrichmentOrchestrator : IDisposable
{
	private readonly ITransport _transport;
	private readonly IAiEnrichmentProvider _provider;
	private readonly AiEnrichmentOptions _options;
	private readonly SemaphoreSlim _throttle;

	private readonly JsonElement _stalenessQuery;
	private readonly string[] _sourceFields;

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
		_stalenessQuery = BuildStalenessQuery();
		_sourceFields = BuildSourceFields();
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
		var skipped = 0;
		var failed = 0;
		var totalCandidates = 0;
		object[]? searchAfter = null;

		while (enriched + failed < _options.MaxEnrichmentsPerRun)
		{
			var remaining = _options.MaxEnrichmentsPerRun - enriched - failed;
			var batchSize = Math.Min(_options.QueryBatchSize, remaining);

			var result = await QueryCandidatesAsync(
				targetIndex, batchSize, searchAfter, ct).ConfigureAwait(false);

			if (result.Candidates.Count == 0)
				break;

			totalCandidates += result.Candidates.Count;
			searchAfter = result.SearchAfter;

			var batch = await ProcessBatchAsync(result.Candidates, ct).ConfigureAwait(false);

			enriched += batch.Enriched;
			skipped += batch.Skipped;
			failed += batch.Failed;
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
			Skipped = skipped,
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
				var orphans = urls.Where(u => !existing.Contains(u)).ToList();

				if (orphans.Count > 0)
					await DeleteByMatchFieldAsync(_provider.LookupIndexName, orphans, ct).ConfigureAwait(false);
			}
		}
		finally
		{
			await pit.DisposeAsync().ConfigureAwait(false);
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
		var exists = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.HEAD, _provider.LookupIndexName, cancellationToken: ct).ConfigureAwait(false);

		if (exists.ApiCallDetails.HttpStatusCode == 200)
			return;

		var put = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.PUT, _provider.LookupIndexName,
			PostData.String(_provider.LookupIndexMapping), cancellationToken: ct).ConfigureAwait(false);

		if (put.ApiCallDetails.HttpStatusCode is not (200 or 201))
			throw new Exception($"Failed to create lookup index '{_provider.LookupIndexName}': HTTP {put.ApiCallDetails.HttpStatusCode}");
	}

	private async Task EnsureEnrichPolicyAsync(CancellationToken ct)
	{
		var exists = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.GET, $"_enrich/policy/{_provider.EnrichPolicyName}",
			cancellationToken: ct).ConfigureAwait(false);

		if (exists.ApiCallDetails.HttpStatusCode == 200)
		{
			// Check if the existing policy matches the current fields hash by
			// inspecting the enrich_fields — if they differ, delete and recreate.
			var existingFieldsMatch = PolicyMatchesCurrentFields(exists);
			if (existingFieldsMatch)
				return;

			await _transport.RequestAsync<JsonResponse>(
				HttpMethod.DELETE, $"_enrich/policy/{_provider.EnrichPolicyName}",
				cancellationToken: ct).ConfigureAwait(false);
		}

		var put = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.PUT, $"_enrich/policy/{_provider.EnrichPolicyName}",
			PostData.String(_provider.EnrichPolicyBody), cancellationToken: ct).ConfigureAwait(false);

		if (put.ApiCallDetails.HttpStatusCode is not (200 or 201))
			throw new Exception($"Failed to create enrich policy '{_provider.EnrichPolicyName}': HTTP {put.ApiCallDetails.HttpStatusCode}");
	}

	private bool PolicyMatchesCurrentFields(JsonResponse response)
	{
		var policies = response.Get<JsonElement>("policies");
		if (policies.ValueKind != JsonValueKind.Array)
			return false;

		foreach (var policy in policies.EnumerateArray())
		{
			if (!policy.TryGetProperty("config", out var config))
				continue;
			if (!config.TryGetProperty("match", out var match))
				continue;
			if (!match.TryGetProperty("enrich_fields", out var fields))
				continue;

			var existingFields = new HashSet<string>();
			foreach (var f in fields.EnumerateArray())
			{
				if (f.GetString() is { } s)
					existingFields.Add(s);
			}

			var expectedFields = new HashSet<string>();
			foreach (var field in _provider.EnrichmentFields)
			{
				expectedFields.Add(field);
				if (_provider.FieldPromptHashFieldNames.TryGetValue(field, out var phField))
					expectedFields.Add(phField);
			}

			return existingFields.SetEquals(expectedFields);
		}

		return false;
	}

	private async Task ExecuteEnrichPolicyAsync(CancellationToken ct)
	{
		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST, $"_enrich/policy/{_provider.EnrichPolicyName}/_execute",
			PostData.Empty, cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception($"Failed to execute enrich policy '{_provider.EnrichPolicyName}': HTTP {response.ApiCallDetails.HttpStatusCode}");
	}

	private async Task EnsurePipelineAsync(CancellationToken ct)
	{
		var expectedTag = $"[fields_hash:{_provider.FieldsHash}]";

		var existing = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.GET, $"_ingest/pipeline/{_provider.PipelineName}",
			cancellationToken: ct).ConfigureAwait(false);

		if (existing.ApiCallDetails.HttpStatusCode == 200)
		{
			var desc = existing.Get<string>($"{_provider.PipelineName}.description");
			if (desc != null && desc.Contains(expectedTag))
				return;
		}

		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.PUT, $"_ingest/pipeline/{_provider.PipelineName}",
			PostData.String(_provider.PipelineBody), cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception($"Failed to create pipeline '{_provider.PipelineName}': HTTP {response.ApiCallDetails.HttpStatusCode}");
	}

	// ── Private: candidate querying ──

	private async Task<CandidateQueryResult> QueryCandidatesAsync(
		string index, int size, object[]? searchAfter, CancellationToken ct)
	{
		var searchAfterClause = searchAfter != null
			? $",\"search_after\":[{string.Join(",", searchAfter.Select(v => $"\"{v}\""))}]"
			: "";

		var sourceJson = string.Join(",", _sourceFields.Select(f => $"\"{f}\""));

		var query = $"{{\"size\":{size},\"query\":{_stalenessQuery.GetRawText()},\"_source\":[{sourceJson}],\"sort\":[{{\"_doc\":\"asc\"}}]{searchAfterClause}}}";
		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST, $"{index}/_search", PostData.String(query), cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			return new CandidateQueryResult([], null);

		return ParseCandidateResponse(response);
	}

	private static CandidateQueryResult ParseCandidateResponse(JsonResponse response)
	{
		var candidates = new List<CandidateDocument>();
		object[]? lastSort = null;

		var hits = response.Get<JsonElement>("hits.hits");
		if (hits.ValueKind != JsonValueKind.Array)
			return new CandidateQueryResult(candidates, null);

		foreach (var hit in hits.EnumerateArray())
		{
			var id = hit.GetProperty("_id").GetString()!;
			var source = hit.GetProperty("_source");
			candidates.Add(new CandidateDocument(id, source.Clone()));

			if (hit.TryGetProperty("sort", out var sort))
				lastSort = sort.EnumerateArray().Select(e => (object)e.ToString()).ToArray();
		}

		return new CandidateQueryResult(candidates, lastSort);
	}

	// ── Private: enrichment processing ──

	private async Task<BatchResult> ProcessBatchAsync(
		List<CandidateDocument> candidates, CancellationToken ct)
	{
		var tasks = candidates.Select(c => EnrichOneAsync(c, ct)).ToList();
		var pendingUpdates = new List<LookupUpdate>();
		var batchSkipped = 0;
		var batchFailed = 0;

#if NET9_0_OR_GREATER
		await foreach (var completedTask in Task.WhenEach(tasks).WithCancellation(ct).ConfigureAwait(false))
		{
			var outcome = await completedTask.ConfigureAwait(false);
			switch (outcome.Status)
			{
				case EnrichmentStatus.Enriched:
					pendingUpdates.Add(outcome.Update!);
					break;
				case EnrichmentStatus.Skipped:
					batchSkipped++;
					break;
				case EnrichmentStatus.Failed:
					batchFailed++;
					break;
			}
		}
#else
		var pending = new HashSet<Task<EnrichmentOutcome>>(tasks);
		while (pending.Count > 0)
		{
			ct.ThrowIfCancellationRequested();
			var completed = await Task.WhenAny(pending).ConfigureAwait(false);
			pending.Remove(completed);
			var outcome = await completed.ConfigureAwait(false);
			switch (outcome.Status)
			{
				case EnrichmentStatus.Enriched:
					pendingUpdates.Add(outcome.Update!);
					break;
				case EnrichmentStatus.Skipped:
					batchSkipped++;
					break;
				case EnrichmentStatus.Failed:
					batchFailed++;
					break;
			}
		}
#endif

		var bulkErrors = 0;
		if (pendingUpdates.Count > 0)
			bulkErrors = await BulkUpsertLookupAsync(pendingUpdates, ct).ConfigureAwait(false);

		return new BatchResult(
			Enriched: pendingUpdates.Count - bulkErrors,
			Skipped: batchSkipped,
			Failed: batchFailed + bulkErrors);
	}

	private async Task<EnrichmentOutcome> EnrichOneAsync(
		CandidateDocument candidate, CancellationToken ct)
	{
		await _throttle.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			var staleFields = DetermineStaleFields(candidate.Source);
			if (staleFields.Count == 0)
				return new EnrichmentOutcome(candidate.Id, EnrichmentStatus.Skipped);

			var prompt = _provider.BuildPrompt(candidate.Source, staleFields);
			if (prompt == null)
				return new EnrichmentOutcome(candidate.Id, EnrichmentStatus.Skipped);

			var completion = await CallCompletionAsync(prompt, ct).ConfigureAwait(false);
			if (completion == null)
				return new EnrichmentOutcome(candidate.Id, EnrichmentStatus.Failed);

			var partialDoc = _provider.ParseResponse(completion, staleFields);
			if (partialDoc == null)
				return new EnrichmentOutcome(candidate.Id, EnrichmentStatus.Failed);

			string? matchValue = null;
			if (candidate.Source.TryGetProperty(_provider.MatchField, out var mv))
				matchValue = mv.GetString();

			if (matchValue == null)
				return new EnrichmentOutcome(candidate.Id, EnrichmentStatus.Skipped);

			var urlHash = UrlHash(matchValue);
			var lookupDoc = BuildLookupDocument(matchValue, partialDoc);
			var update = new LookupUpdate(urlHash, lookupDoc);
			return new EnrichmentOutcome(candidate.Id, EnrichmentStatus.Enriched, update);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			return new EnrichmentOutcome(candidate.Id, EnrichmentStatus.Failed);
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

			if (!source.TryGetProperty(phField, out var existingHash) || existingHash.ValueKind != JsonValueKind.String || existingHash.GetString() != currentHash)
				stale.Add(field);
		}
		return stale;
	}

	private async Task<string?> CallCompletionAsync(string prompt, CancellationToken ct)
	{
		var url = $"_inference/completion/{_options.InferenceEndpointId}";

		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST, url,
			PostData.Serializable(new CompletionRequest { Input = prompt }),
			cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			return null;

		return ExtractCompletionText(response);
	}

	private static string? ExtractCompletionText(JsonResponse response)
	{
		var result = response.Get<string>("completion.0.result");
		return string.IsNullOrEmpty(result) ? null : result;
	}

	// ── Private: lookup index updates ──

	private JsonElement BuildLookupDocument(string matchValue, string partialDocJson)
	{
		var now = DateTimeOffset.UtcNow.UtcDateTime.ToString(
			"yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

		var sb = new StringBuilder();
		sb.Append('{');
		sb.Append('"').Append(_provider.MatchField).Append("\":");
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
		PostData body;

#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
		var items = updates.ToArray();
		var bytes = BulkRequestDataFactory.GetBytes(
			items.AsSpan(),
			IngestChannelStatics.SerializerOptions,
			static u => new UpdateOperation { Id = u.UrlHash },
			static u => u.Document);
		body = PostData.ReadOnlyMemory(bytes);
#else
		var sb = new StringBuilder();
		foreach (var update in updates)
		{
			sb.Append("{\"update\":{\"_id\":\"").Append(update.UrlHash).Append("\"}}\n");
			sb.Append("{\"doc_as_upsert\":true,\"doc\":").Append(update.Document.GetRawText()).Append("}\n");
		}
		body = PostData.String(sb.ToString());
#endif

		var response = await _transport.RequestAsync<BulkResponse>(
			HttpMethod.POST,
			$"{_provider.LookupIndexName}/_bulk?filter_path=errors,items.*.status,items.*.error",
			body, cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			return updates.Count;

		var errors = 0;
		foreach (var item in response.Items)
		{
			if (item.Status < 200 || item.Status > 299)
				errors++;
		}
		return errors;
	}

	// ── Private: backfill ──

	private async Task BackfillAsync(string targetIndex, CancellationToken ct)
	{
		var url = $"/{targetIndex}/_update_by_query?wait_for_completion=false&pipeline={_provider.PipelineName}";

		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST, url,
			PostData.Serializable(new QueryRequest { Query = _stalenessQuery }),
			cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			return;

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
			+ $",\"_source\":[\"{_provider.MatchField}\"]"
			+ $",\"query\":{{\"terms\":{{\"{_provider.MatchField}\":[{terms}]}}}}"
			+ $",\"collapse\":{{\"field\":\"{_provider.MatchField}\"}}"
			+ "}";

		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST, $"{targetIndex}/_search", PostData.String(query), cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			return existing;

		var hits = response.Get<JsonElement>("hits.hits");
		if (hits.ValueKind != JsonValueKind.Array)
			return existing;

		foreach (var hit in hits.EnumerateArray())
		{
			if (hit.TryGetProperty("_source", out var source)
				&& source.TryGetProperty(_provider.MatchField, out var urlProp)
				&& urlProp.GetString() is { } url)
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
		var deleteQuery = $"{{\"terms\":{{\"{_provider.MatchField}\":[{terms}]}}}}";
		var dbq = new DeleteByQuery(_transport, new DeleteByQueryOptions
		{
			Index = index,
			QueryBody = deleteQuery
		});
		await dbq.RunAsync(ct).ConfigureAwait(false);
	}

	// ── Private: query building (cached) ──

	private JsonElement BuildStalenessQuery()
	{
		var clauses = new List<string>();
		foreach (var field in _provider.EnrichmentFields)
		{
			clauses.Add($"{{\"bool\":{{\"must_not\":{{\"exists\":{{\"field\":\"{field}\"}}}}}}}}");

			if (_provider.FieldPromptHashFieldNames.TryGetValue(field, out var phField)
				&& _provider.FieldPromptHashes.TryGetValue(field, out var phValue))
				clauses.Add($"{{\"bool\":{{\"must_not\":{{\"term\":{{\"{phField}\":\"{phValue}\"}}}}}}}}");
		}
		var json = $"{{\"bool\":{{\"should\":[{string.Join(",", clauses)}],\"minimum_should_match\":1}}}}";
		return JsonDocument.Parse(json).RootElement.Clone();
	}

	private string[] BuildSourceFields()
	{
		var fields = new List<string>(_provider.RequiredSourceFields) { _provider.MatchField };
		foreach (var phField in _provider.FieldPromptHashFieldNames.Values)
			fields.Add(phField);
		return fields.ToArray();
	}

	// ── Private: helpers ──

	private static string UrlHash(string url)
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
