// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
///   <item><see cref="EnrichAsync"/> — stream progress as ES|QL COMPLETION queries run, update lookup, backfill</item>
///   <item><see cref="CleanupOrphanedAsync"/>, <see cref="CleanupOlderThanAsync"/>, <see cref="PurgeAsync"/> — manage stale cache entries</item>
/// </list>
/// </summary>
public class AiEnrichmentOrchestrator : IDisposable
{
	private readonly ITransport _transport;
	private readonly IAiEnrichmentProvider _provider;
	private readonly AiInfrastructure _infra;

	private readonly JsonElement _stalenessQuery;

	/// <summary>
	/// Creates the orchestrator from an <see cref="ElasticsearchTypeContext"/> that carries an
	/// <see cref="IAiEnrichmentProvider"/>. The lookup index name is derived from the context's
	/// write target (<c>{writeTarget}-ai-cache</c>).
	/// </summary>
	public AiEnrichmentOrchestrator(ITransport transport, ElasticsearchTypeContext context)
	{
		var provider = context.AiEnrichmentProvider
			?? throw new ArgumentException(
				"Context does not carry an AiEnrichmentProvider. " +
				"Ensure the document type has an [AiEnrichment<T>] attribute.", nameof(context));
		var writeTarget = context.IndexStrategy?.WriteTarget
			?? throw new InvalidOperationException("No write target configured on the context.");
		var infra = provider.CreateInfrastructure($"{writeTarget}-ai-cache");

		_transport = transport ?? throw new ArgumentNullException(nameof(transport));
		_provider = provider;
		_infra = infra;
		_stalenessQuery = BuildStalenessQuery();
	}

	/// <inheritdoc cref="AiEnrichmentOrchestrator"/>
	public AiEnrichmentOrchestrator(ITransport transport, IAiEnrichmentProvider provider)
		: this(transport, provider, null) { }

	/// <summary>
	/// Creates the orchestrator with explicit infrastructure names.
	/// Use <paramref name="infrastructure"/> when the lookup index name must be resolved at runtime
	/// (e.g., derived from a <c>CreateContext</c>-resolved write target to namespace per environment).
	/// When <c>null</c>, the provider's compile-time default names are used.
	/// </summary>
	public AiEnrichmentOrchestrator(
		ITransport transport,
		IAiEnrichmentProvider provider,
		AiInfrastructure? infrastructure)
	{
		_transport = transport ?? throw new ArgumentNullException(nameof(transport));
		_provider = provider ?? throw new ArgumentNullException(nameof(provider));
		_infra = infrastructure ?? AiInfrastructure.FromProvider(provider);
		_stalenessQuery = BuildStalenessQuery();
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
	/// Post-indexing: streams <see cref="AiEnrichmentProgress"/> as enrichment proceeds.
	/// Queries the target index for documents needing enrichment, sends parallel ES|QL
	/// COMPLETION queries (yielding per-chunk progress), stores results in the lookup
	/// index, then re-executes the enrich policy and backfills only the enriched documents.
	/// <para>
	/// Set <see cref="AiEnrichmentOptions.Timeout"/> for time-boxed runs (e.g. CI jobs).
	/// The orchestrator stops new work at <c>Timeout − DrainTimeout</c> and uses the
	/// remaining grace window for draining in-flight requests and backfilling.
	/// </para>
	/// <para>
	/// The <paramref name="ct"/> token also triggers the soft-stop (e.g. application shutdown),
	/// while the hard timeout boundary protects the drain+backfill phase.
	/// Without a <see cref="AiEnrichmentOptions.Timeout"/>, <paramref name="ct"/> is a
	/// traditional hard cancel that aborts everything.
	/// </para>
	/// </summary>
	public async IAsyncEnumerable<AiEnrichmentProgress> EnrichAsync(
		string targetIndex,
		AiEnrichmentOptions? options = null,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		var opts = options ?? new AiEnrichmentOptions();

		var drainGrace = opts.DrainTimeout ?? (opts.CompletionTimeout + TimeSpan.FromSeconds(30));
		var softDelay = opts.Timeout.HasValue ? opts.Timeout.Value - drainGrace : TimeSpan.Zero;

		using var softCts = opts.Timeout.HasValue && softDelay > TimeSpan.Zero
			? CreateTimeoutCts(opts, softDelay) : null;
		using var hardCts = opts.Timeout.HasValue
			? CreateTimeoutCts(opts, opts.Timeout.Value) : null;

		var stopToken = softCts?.Token ?? CancellationToken.None;
		var hardToken = hardCts?.Token ?? ct;

		var enriched = 0;
		var failed = 0;
		var totalCandidates = 0;

		yield return Progress(AiEnrichmentPhase.Querying, enriched, failed, totalCandidates,
			$"Querying up to {opts.MaxEnrichmentsPerRun} candidates...");

		var candidateIds = await QueryCandidateIdsAsync(targetIndex, opts, ct).ConfigureAwait(false);
		totalCandidates = candidateIds.Count;

		if (totalCandidates == 0)
		{
			yield return Progress(AiEnrichmentPhase.Complete, 0, 0, 0, "No candidates found");
			yield break;
		}

		yield return Progress(AiEnrichmentPhase.Querying, enriched, failed, totalCandidates,
			$"Found {totalCandidates} candidates, sending ES|QL COMPLETION queries...");

		// 2. Chunk IDs and run parallel ES|QL COMPLETION queries, yielding per-chunk
		var chunks = ChunkList(candidateIds, opts.EsqlBatchSize);
		var totalChunks = chunks.Count;
		var allUpdates = new List<LookupUpdate>();
		var retryDelay = TimeSpan.FromSeconds(Math.Min(opts.CompletionTimeout.TotalSeconds, 30));
		var minBatch = Math.Max(opts.MinCompletionBatchSize, 1);

		var allEnrichedDocIds = new List<string>();
		var activeTasks = new Dictionary<Task<EsqlChunkResult>, ChunkMeta>();
		var pendingChunks = new Queue<ChunkMeta>();
		for (var i = 0; i < chunks.Count; i++)
			pendingChunks.Enqueue(new ChunkMeta(chunks[i], 0));

		while (pendingChunks.Count > 0 && activeTasks.Count < opts.EsqlConcurrency
			&& !stopToken.IsCancellationRequested && !ct.IsCancellationRequested)
		{
			var meta = pendingChunks.Dequeue();
			var task = RunEsqlCompletionAsync(targetIndex, meta.DocIds, opts, hardToken);
			activeTasks[task] = meta;
		}

		var stopping = stopToken.IsCancellationRequested || ct.IsCancellationRequested;
		var completedChunks = 0;
		while (activeTasks.Count > 0)
		{
			hardToken.ThrowIfCancellationRequested();

			if (!stopping)
				stopping = stopToken.IsCancellationRequested || ct.IsCancellationRequested;

			var completed = await Task.WhenAny(activeTasks.Keys).ConfigureAwait(false);
			var meta = activeTasks[completed];
			activeTasks.Remove(completed);

			var result = await completed.ConfigureAwait(false);

			if (result.Error != null && result.IsRetryable
				&& meta.Depth < opts.CompletionMaxRetries && !stopping)
			{
				var currentSize = meta.DocIds.Count;
				var retrySize = Math.Max((currentSize + 1) / 2, minBatch);

				if (retrySize < currentSize)
				{
					var subChunks = ChunkList(meta.DocIds, retrySize);
					var newDepth = meta.Depth + 1;

					yield return Progress(AiEnrichmentPhase.Enriching, enriched, failed, totalCandidates,
						$"Chunk timed out ({currentSize} docs) — splitting into {subChunks.Count} × {retrySize} (depth {newDepth}/{opts.CompletionMaxRetries}), waiting {retryDelay.TotalSeconds:N0}s...");

					foreach (var sub in subChunks)
						pendingChunks.Enqueue(new ChunkMeta(sub, newDepth));
					totalChunks += subChunks.Count - 1;

					await SchedulePendingAfterDelayAsync(
						targetIndex, opts, retryDelay, pendingChunks, activeTasks, hardToken).ConfigureAwait(false);
					continue;
				}

				yield return Progress(AiEnrichmentPhase.Enriching, enriched, failed, totalCandidates,
					$"Chunk failed ({currentSize} docs, already at minimum batch size): {result.Error}");
				failed += meta.DocIds.Count;
			}
			else if (result.Error != null)
			{
				failed += meta.DocIds.Count;
				completedChunks++;
				var depthNote = meta.Depth > 0 ? $" (depth {meta.Depth})" : "";
				yield return Progress(AiEnrichmentPhase.Enriching, enriched, failed, totalCandidates,
					$"Chunk {completedChunks}/{totalChunks}: {result.Error}{depthNote}");
			}
			else
			{
				completedChunks++;
				allUpdates.AddRange(result.Updates);
				allEnrichedDocIds.AddRange(result.EnrichedDocIds);
				failed += result.Failed;
				enriched = allUpdates.Count;

				var depthNote = meta.Depth > 0 ? $" (depth {meta.Depth})" : "";
				yield return Progress(AiEnrichmentPhase.Enriching, enriched, failed, totalCandidates,
					$"Chunk {completedChunks}/{totalChunks}: +{result.Updates.Count} enriched, +{result.Failed} failed{depthNote}");
			}

			if (!stopping)
				stopping = stopToken.IsCancellationRequested || ct.IsCancellationRequested;

			if (!stopping)
			{
				while (pendingChunks.Count > 0 && activeTasks.Count < opts.EsqlConcurrency)
				{
					var next = pendingChunks.Dequeue();
					var task = RunEsqlCompletionAsync(targetIndex, next.DocIds, opts, hardToken);
					activeTasks[task] = next;
				}
			}
		}

		if (stopping && pendingChunks.Count > 0)
		{
			var skippedChunks = pendingChunks.Count;
			var skippedDocs = 0;
			foreach (var p in pendingChunks)
				skippedDocs += p.DocIds.Count;
			pendingChunks.Clear();
			yield return Progress(AiEnrichmentPhase.Draining, enriched, failed, totalCandidates,
				$"Graceful stop: skipped {skippedChunks} pending chunk(s) ({skippedDocs} docs)");
		}

		// 3. Bulk upsert to lookup index
		var bulkErrors = 0;
		if (allUpdates.Count > 0)
			bulkErrors = await BulkUpsertLookupAsync(allUpdates, hardToken).ConfigureAwait(false);

		enriched -= bulkErrors;
		failed += bulkErrors;

		// 4. Refresh + execute policy + backfill
		if (enriched > 0)
		{
			yield return Progress(AiEnrichmentPhase.Refreshing, enriched, failed, totalCandidates,
				$"Refreshing lookup index '{_infra.LookupIndexName}'...");
			await RefreshAsync(_infra.LookupIndexName, hardToken).ConfigureAwait(false);

			yield return Progress(AiEnrichmentPhase.ExecutingPolicy, enriched, failed, totalCandidates,
				$"Executing enrich policy '{_infra.EnrichPolicyName}'...");
			await ExecuteEnrichPolicyAsync(hardToken).ConfigureAwait(false);

			if (!opts.SkipBackfill)
			{
				yield return Progress(AiEnrichmentPhase.Backfilling, enriched, failed, totalCandidates,
					$"Backfilling {enriched} enriched docs into '{targetIndex}'...");
				await BackfillAsync(targetIndex, allEnrichedDocIds, hardToken).ConfigureAwait(false);
			}
		}

		yield return Progress(AiEnrichmentPhase.Complete, enriched, failed, totalCandidates, null);
	}

	/// <summary>
	/// Applies the enrich pipeline to <b>all</b> documents in the target index via a single
	/// <c>_update_by_query</c> that runs asynchronously on the server. Progress is yielded
	/// on each task-poll so callers can report throughput.
	/// <para>
	/// Use this after <see cref="InitializeAsync"/> to force a complete backfill, or as
	/// a one-off catch-up when documents were indexed before the pipeline existed.
	/// </para>
	/// <para><b>Example:</b></para>
	/// <code>
	/// await foreach (var p in orchestrator.BackfillAllAsync("my-index"))
	///     logger.LogInformation("[{Phase}] {Message}", p.Phase, p.Message);
	/// </code>
	/// </summary>
	/// <param name="targetIndex">The index to update.</param>
	/// <param name="pollInterval">How often to poll the background task. Default: 5 s.</param>
	/// <param name="ct">Cancellation token.</param>
	public async IAsyncEnumerable<AiEnrichmentProgress> BackfillAllAsync(
		string targetIndex,
		TimeSpan? pollInterval = null,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		var interval = pollInterval ?? TimeSpan.FromSeconds(5);

		yield return Progress(AiEnrichmentPhase.Backfilling, 0, 0, 0,
			$"Starting full backfill on '{targetIndex}' via pipeline '{_infra.PipelineName}'...");

		var url = $"/{targetIndex}/_update_by_query?wait_for_completion=false&pipeline={_infra.PipelineName}";
		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST, url,
			PostData.Serializable(new QueryRequest { Query = JsonDocument.Parse("{\"match_all\":{}}").RootElement.Clone() }),
			cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
		{
			yield return Progress(AiEnrichmentPhase.Complete, 0, 0, 0,
				$"_update_by_query failed: HTTP {response.ApiCallDetails.HttpStatusCode}");
			yield break;
		}

		var taskId = response.Get<string>("task");
		if (string.IsNullOrEmpty(taskId))
		{
			yield return Progress(AiEnrichmentPhase.Complete, 0, 0, 0, "No task ID returned.");
			yield break;
		}

		await foreach (var taskResponse in ElasticsearchTaskMonitor.PollTaskAsync(
			_transport, taskId, interval, ct).ConfigureAwait(false))
		{
			var updated = taskResponse.Get<int?>("task.status.updated") ?? 0;
			var total = taskResponse.Get<int?>("task.status.total") ?? 0;
			var created = taskResponse.Get<int?>("task.status.created") ?? 0;
			var failures = taskResponse.Get<int?>("task.status.version_conflicts") ?? 0;
			var completed = taskResponse.Get<bool>("completed");

			yield return Progress(AiEnrichmentPhase.Backfilling, updated, failures, total,
				$"Backfilling: {updated + created}/{total} docs processed"
				+ (completed ? " — done." : "..."));
		}

		yield return Progress(AiEnrichmentPhase.Complete, 0, 0, 0, "Full backfill completed.");
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
			Index = _infra.LookupIndexName,
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
					if (source.TryGetProperty(_infra.MatchField, out var urlProp)
						&& urlProp.GetString() is { } url)
						urls.Add(url);
				}

				if (urls.Count == 0)
					continue;

				var existing = await FindExistingUrlsAsync(targetIndex, urls, ct).ConfigureAwait(false);
				var orphans = urls.Where(u => !existing.Contains(u)).ToList();

				if (orphans.Count > 0)
					await DeleteByMatchFieldAsync(_infra.LookupIndexName, orphans, ct).ConfigureAwait(false);
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
			Index = _infra.LookupIndexName,
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
			Index = _infra.LookupIndexName,
			QueryBody = "{\"match_all\":{}}"
		});
		await dbq.RunAsync(ct).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public void Dispose() => GC.SuppressFinalize(this);

	// ── Private: initialization ──

	private async Task EnsureLookupIndexAsync(CancellationToken ct)
	{
		var exists = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.HEAD, _infra.LookupIndexName, cancellationToken: ct).ConfigureAwait(false);

		if (exists.ApiCallDetails.HttpStatusCode == 200)
			return;

		var put = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.PUT, _infra.LookupIndexName,
			PostData.String(_infra.LookupIndexMapping), cancellationToken: ct).ConfigureAwait(false);

		if (put.ApiCallDetails.HttpStatusCode is not (200 or 201))
			throw new Exception($"Failed to create lookup index '{_infra.LookupIndexName}': HTTP {put.ApiCallDetails.HttpStatusCode}");
	}

	private async Task EnsureEnrichPolicyAsync(CancellationToken ct)
	{
		var exists = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.GET, $"_enrich/policy/{_infra.EnrichPolicyName}",
			cancellationToken: ct).ConfigureAwait(false);

		if (exists.ApiCallDetails.HttpStatusCode == 200)
		{
			var matchResult = PolicyMatchesCurrentFields(exists);
			if (matchResult == PolicyMatch.Matches)
				return;

			if (matchResult == PolicyMatch.SchemaChanged)
			{
				await _transport.RequestAsync<StringResponse>(
					HttpMethod.DELETE, $"_ingest/pipeline/{_infra.PipelineName}",
					cancellationToken: ct).ConfigureAwait(false);

				var del = await _transport.RequestAsync<StringResponse>(
					HttpMethod.DELETE, $"_enrich/policy/{_infra.EnrichPolicyName}",
					cancellationToken: ct).ConfigureAwait(false);

				if (del.ApiCallDetails.HttpStatusCode is not (200 or 404))
					throw new Exception(
						$"Failed to delete stale enrich policy '{_infra.EnrichPolicyName}': " +
						$"HTTP {del.ApiCallDetails.HttpStatusCode} — {del.Body}");
			}
		}

		var put = await _transport.RequestAsync<StringResponse>(
			HttpMethod.PUT, $"_enrich/policy/{_infra.EnrichPolicyName}",
			PostData.String(_infra.EnrichPolicyBody), cancellationToken: ct).ConfigureAwait(false);

		if (put.ApiCallDetails.HttpStatusCode is not (200 or 201))
			throw new Exception(
				$"Failed to create enrich policy '{_infra.EnrichPolicyName}': " +
				$"HTTP {put.ApiCallDetails.HttpStatusCode} — {put.Body}");
	}

	private enum PolicyMatch { NotFound, Matches, SchemaChanged }

	private PolicyMatch PolicyMatchesCurrentFields(JsonResponse response)
	{
		if (response.Body is not JsonObject root)
			return PolicyMatch.NotFound;

		var policies = root["policies"]?.AsArray();
		if (policies == null || policies.Count == 0)
			return PolicyMatch.NotFound;

		foreach (var policy in policies)
		{
			var match = policy?["config"]?["match"];
			if (match == null)
				continue;

			var indicesNode = match["indices"];
			if (indicesNode != null)
			{
				var indexName = indicesNode is JsonArray arr
					? arr.Count == 1 ? arr[0]?.GetValue<string>() : null
					: indicesNode.GetValue<string>();

				if (indexName != _infra.LookupIndexName)
					return PolicyMatch.SchemaChanged;
			}

			var fields = match["enrich_fields"]?.AsArray();
			if (fields == null)
				continue;

			var existingFields = new HashSet<string>();
			foreach (var f in fields)
			{
				if (f?.GetValue<string>() is { } s)
					existingFields.Add(s);
			}

			var expectedFields = new HashSet<string>();
			foreach (var field in _provider.EnrichmentFields)
			{
				expectedFields.Add(field);
				if (_provider.FieldPromptHashFieldNames.TryGetValue(field, out var phField))
					expectedFields.Add(phField);
			}

			return existingFields.SetEquals(expectedFields)
				? PolicyMatch.Matches
				: PolicyMatch.SchemaChanged;
		}

		return PolicyMatch.NotFound;
	}

	private async Task ExecuteEnrichPolicyAsync(CancellationToken ct)
	{
		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST, $"_enrich/policy/{_infra.EnrichPolicyName}/_execute",
			PostData.Empty, cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception($"Failed to execute enrich policy '{_infra.EnrichPolicyName}': HTTP {response.ApiCallDetails.HttpStatusCode}");
	}

	private async Task EnsurePipelineAsync(CancellationToken ct)
	{
		var expectedTag = $"[fields_hash:{_infra.FieldsHash}]";

		var existing = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.GET, $"_ingest/pipeline/{_infra.PipelineName}",
			cancellationToken: ct).ConfigureAwait(false);

		if (existing.ApiCallDetails.HttpStatusCode == 200 && existing.Body is JsonObject pipelineRoot)
		{
			var desc = pipelineRoot[_infra.PipelineName]?["description"]?.GetValue<string>();
			if (desc != null && desc.Contains(expectedTag))
				return;
		}

		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.PUT, $"_ingest/pipeline/{_infra.PipelineName}",
			PostData.String(_infra.PipelineBody), cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception($"Failed to create pipeline '{_infra.PipelineName}': HTTP {response.ApiCallDetails.HttpStatusCode}");
	}

	// ── Private: candidate ID querying ──

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

	// ── Private: ES|QL COMPLETION ──

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
		var body = sb.ToString();

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

		JsonNode? rootNode;
		try { rootNode = JsonNode.Parse(response.Body); }
		catch { return new EsqlChunkResult(updates, enrichedDocIds, docIds.Count, "ES|QL response was not valid JSON"); }

		if (rootNode is not JsonObject root)
			return new EsqlChunkResult(updates, enrichedDocIds, docIds.Count, "ES|QL response root was not a JSON object");

		var columns = root["columns"]?.AsArray();
		var values = root["values"]?.AsArray();
		if (columns == null || values == null)
			return new EsqlChunkResult(updates, enrichedDocIds, docIds.Count,
				$"ES|QL response missing columns/values: {Truncate(response.Body, 500)}");

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

	private static CancellationTokenSource CreateTimeoutCts(
		AiEnrichmentOptions opts, TimeSpan delay)
	{
#if NET8_0_OR_GREATER
		var cts = new TimeProviderCts(opts.TimeProvider, delay);
		return cts;
#else
		return new CancellationTokenSource(delay);
#endif
	}

#if NET8_0_OR_GREATER
	private sealed class TimeProviderCts : CancellationTokenSource
	{
		private readonly ITimer _timer;

		public TimeProviderCts(TimeProvider timeProvider, TimeSpan delay)
		{
			_timer = timeProvider.CreateTimer(
				static s => ((CancellationTokenSource)s!).Cancel(), this,
				delay, Timeout.InfiniteTimeSpan);
		}

		protected override void Dispose(bool disposing)
		{
			_timer.Dispose();
			base.Dispose(disposing);
		}
	}
#endif

	private static List<List<T>> ChunkList<T>(List<T> source, int chunkSize)
	{
		var chunks = new List<List<T>>();
		for (var i = 0; i < source.Count; i += chunkSize)
		{
			var size = Math.Min(chunkSize, source.Count - i);
			chunks.Add(source.GetRange(i, size));
		}
		return chunks;
	}

	// ── Private: lookup index updates ──

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

	// ── Private: backfill ──

	// Elasticsearch ids query uses terms on _id under the hood (default
	// index.max_terms_count = 65 536).  We batch well under that to keep
	// individual _update_by_query requests small and predictable.
	private const int BackfillBatchSize = 1_000;

	private async Task BackfillAsync(string targetIndex, List<string> enrichedDocIds, CancellationToken ct)
	{
		if (enrichedDocIds.Count == 0)
			return;

		var url = $"/{targetIndex}/_update_by_query?wait_for_completion=false&pipeline={_infra.PipelineName}";
		var batches = ChunkList(enrichedDocIds, BackfillBatchSize);

		foreach (var batch in batches)
		{
			var idsJson = string.Join(",", batch.Select(id => $"\"{JsonEncodedText.Encode(id)}\""));
			var idsQuery = JsonDocument.Parse($"{{\"ids\":{{\"values\":[{idsJson}]}}}}").RootElement.Clone();

			var response = await _transport.RequestAsync<JsonResponse>(
				HttpMethod.POST, url,
				PostData.Serializable(new QueryRequest { Query = idsQuery }),
				cancellationToken: ct).ConfigureAwait(false);

			if (response.ApiCallDetails.HttpStatusCode is not 200)
				continue;

			var taskId = response.Get<string>("task");
			if (!string.IsNullOrEmpty(taskId))
			{
				await foreach (var _ in ElasticsearchTaskMonitor.PollTaskAsync(
					_transport, taskId, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false))
				{
				}
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

	// ── Private: helpers ──

	private static AiEnrichmentProgress Progress(
		AiEnrichmentPhase phase, int enriched, int failed, int totalCandidates, string? message) =>
		new()
		{
			Phase = phase,
			Enriched = enriched,
			Failed = failed,
			TotalCandidates = totalCandidates,
			Message = message
		};

	private static string Truncate(string? value, int maxLength)
	{
		if (value == null) return "(null)";
		if (value.Length <= maxLength) return value;
#if NET8_0_OR_GREATER
		return string.Concat(value.AsSpan(0, maxLength), "…");
#else
		return value.Substring(0, maxLength) + "…";
#endif
	}

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
