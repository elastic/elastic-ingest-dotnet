// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
///   <item><see cref="EnrichAsync"/> — stream progress as ES|QL COMPLETION queries run, update lookup, backfill</item>
///   <item><see cref="CleanupOrphanedAsync"/>, <see cref="CleanupOlderThanAsync"/>, <see cref="PurgeAsync"/> — manage stale cache entries</item>
/// </list>
/// </summary>
public partial class AiEnrichmentOrchestrator : IDisposable
{
	private readonly ITransport _transport;
	private readonly IAiEnrichmentProvider _provider;
	private readonly AiInfrastructure _infra;
	private readonly string _versionedPolicyName;

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
		_versionedPolicyName = $"{infra.EnrichPolicyName}-{infra.FieldsHash}";
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
		_versionedPolicyName = $"{_infra.EnrichPolicyName}-{_infra.FieldsHash}";
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
		await CleanupStalePoliciesAsync(ct).ConfigureAwait(false);
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

		// ── 1. Discover candidates ──
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

		// ── 2. Concurrent ES|QL COMPLETION with retry/split ──
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

		// ── 3. Persist to lookup index ──
		var bulkErrors = 0;
		if (allUpdates.Count > 0)
			bulkErrors = await BulkUpsertLookupAsync(allUpdates, hardToken).ConfigureAwait(false);

		enriched -= bulkErrors;
		failed += bulkErrors;

		// ── 4. Refresh + execute policy + backfill ──
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

		var matchAll = JsonDocument.Parse("{\"match_all\":{}}").RootElement.Clone();

		await foreach (var taskResponse in RunUpdateByQueryWithPollingAsync(
			targetIndex, matchAll, ct, interval).ConfigureAwait(false))
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

	// ── Shared helpers ──

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

	private static CancellationTokenSource CreateTimeoutCts(
		AiEnrichmentOptions opts, TimeSpan delay)
	{
#if NET8_0_OR_GREATER
		return new TimeProviderCts(opts.TimeProvider, delay);
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
}
