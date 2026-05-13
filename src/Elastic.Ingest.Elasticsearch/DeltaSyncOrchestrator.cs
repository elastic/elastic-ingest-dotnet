// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Channels.Diagnostics;
using Elastic.Ingest.Elasticsearch.Helpers;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Strategies;
using Elastic.Mapping;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// Orchestrates two Elasticsearch channels (primary + secondary) for delta-only sources that emit
/// additions and updates but never a full snapshot.
/// <para>
/// Unlike <see cref="IncrementalSyncOrchestrator{TEvent}"/>, this orchestrator does NOT use
/// <c>batch_index_date</c> to reap stale documents — not re-sending a document in a run means it
/// stays in the index, not that it was deleted.
/// </para>
/// <para>
/// When a mapping hash change causes a rollover (new backing index), <see cref="StartAsync"/>
/// detects it and populates <see cref="DeltaOrchestratorContext{TEvent}.PendingRolloverBackfills"/>.
/// Consumers must call <see cref="BackfillRolledOverIndicesAsync"/> before any <see cref="TryWrite"/>
/// to copy historical data into the new index. The method is safe to call unconditionally — it is a
/// noop when no rollover occurred.
/// </para>
/// </summary>
public class DeltaSyncOrchestrator<TEvent> : ISyncOrchestrator<TEvent>
	where TEvent : class
{
	private readonly ITransport _transport;
	private readonly ElasticsearchTypeContext _primaryTypeContext;
	private readonly ElasticsearchTypeContext _secondaryTypeContext;
	private readonly DateTimeOffset _batchTimestamp = DateTimeOffset.UtcNow;
	private readonly List<Func<ITransport, CancellationToken, Task>> _preBootstrapTasks = new();
	private readonly Action<TEvent, DateTimeOffset>? _setBatchIndexDate;
	private readonly Action<TEvent, DateTimeOffset>? _setLastUpdated;

	private IngestChannel<TEvent>? _primaryChannel;
	private IngestChannel<TEvent>? _secondaryChannel;
	private IngestSyncStrategy _strategy = IngestSyncStrategy.Multiplex;
	private List<RolloverBackfillTask> _pendingRolloverBackfills = [];

	// Write aliases that need an atomic swap in CompleteAsync because bootstrap created a new
	// backing index alongside an old one, leaving the write alias pointing to both.
	private readonly List<(string WriteAlias, string WriteTarget, string NewIndex)> _writeAliasSwaps = [];

	private string? _primaryWriteAlias;
	private string? _secondaryWriteAlias;
	private string? _primaryIndexName;
	private string? _secondaryIndexName;
	private string? _secondaryReindexTarget;
	private DateTimeOffset _reindexCutoff;
	private DeltaOrchestratorContext<TEvent>? _context;

	/// <summary>
	/// Creates the orchestrator from two <see cref="IStaticMappingResolver{T}"/> instances.
	/// Throws <see cref="ArgumentException"/> if the primary resolver lacks <c>[BatchIndexDate]</c>
	/// or <c>[LastUpdated]</c> setters (required for the <c>reindex-updates</c> cutoff).
	/// </summary>
	public DeltaSyncOrchestrator(
		ITransport transport,
		IStaticMappingResolver<TEvent> primary,
		IStaticMappingResolver<TEvent> secondary)
	{
		EnsureHasBatchFields(primary.SetBatchIndexDate, primary.SetLastUpdated, nameof(primary));
		_transport = transport;
		_setBatchIndexDate = primary.SetBatchIndexDate;
		_setLastUpdated = primary.SetLastUpdated;
		BatchIndexDateField = primary.BatchIndexDateFieldName ?? "batch_index_date";
		LastUpdatedField = primary.LastUpdatedFieldName ?? "last_updated";
		_primaryTypeContext = primary.Context with { IndexPatternUseBatchDate = true };
		_secondaryTypeContext = secondary.Context with { IndexPatternUseBatchDate = true };
	}

	/// <summary>
	/// Creates the orchestrator from two raw <see cref="ElasticsearchTypeContext"/> instances.
	/// Throws <see cref="ArgumentException"/> if the primary context lacks <c>[BatchIndexDate]</c>
	/// or <c>[LastUpdated]</c> setters (required for the <c>reindex-updates</c> cutoff).
	/// </summary>
	public DeltaSyncOrchestrator(
		ITransport transport,
		ElasticsearchTypeContext primary,
		ElasticsearchTypeContext secondary)
	{
		EnsureHasBatchFields(primary.SetBatchIndexDate, primary.SetLastUpdated, nameof(primary));
		_transport = transport;
		_primaryTypeContext = primary with { IndexPatternUseBatchDate = true };
		_secondaryTypeContext = secondary with { IndexPatternUseBatchDate = true };
		_setBatchIndexDate = primary.SetBatchIndexDate;
		_setLastUpdated = primary.SetLastUpdated;
		if (primary.BatchIndexDateFieldName != null) BatchIndexDateField = primary.BatchIndexDateFieldName;
		if (primary.LastUpdatedFieldName != null) LastUpdatedField = primary.LastUpdatedFieldName;
	}

	// ── Init-only knobs ──────────────────────────────────────────────────

	/// <summary>The field name used for last-updated range queries in the reindex-updates step.</summary>
	public string LastUpdatedField { get; init; } = "last_updated";

	/// <summary>The field name stamped on each document for observability and the reindex-updates cutoff.</summary>
	public string BatchIndexDateField { get; init; } = "batch_index_date";

	/// <summary>Optional configuration callback for the primary channel options.</summary>
	public Action<IngestChannelOptions<TEvent>>? ConfigurePrimary { get; init; }

	/// <summary>Optional configuration callback for the secondary channel options.</summary>
	public Action<IngestChannelOptions<TEvent>>? ConfigureSecondary { get; init; }

	/// <summary>Optional hook that runs after <see cref="CompleteAsync"/> finishes all operations.</summary>
	public Func<DeltaOrchestratorContext<TEvent>, ITransport, CancellationToken, Task>? OnPostComplete { get; init; }

	/// <summary>
	/// Callback invoked on each progress poll during a server-side <c>_reindex</c> step.
	/// The string parameter is a label identifying the step (e.g. <c>"reindex-updates"</c>,
	/// <c>"rollover-backfill-primary"</c>).
	/// </summary>
	public Action<string, ReindexProgress>? OnReindexProgress { get; init; }

	/// <summary>
	/// Callback invoked when a rollover decision is made for an index during <see cref="StartAsync"/>.
	/// Fired once for primary and once for secondary.
	/// </summary>
	public Action<IndexRolloverInfo>? OnRolloverDecision { get; init; }

	// ── Diagnostics ─────────────────────────────────────────────────────

	/// <summary>The resolved ingest strategy after <see cref="StartAsync"/> completes.</summary>
	public IngestSyncStrategy Strategy => _strategy;

	/// <summary>The batch timestamp assigned when the orchestrator was created.</summary>
	public DateTimeOffset BatchTimestamp => _batchTimestamp;

	/// <inheritdoc />
	public IChannelDiagnosticsListener? DiagnosticsListener => null;

	// ── Lifecycle ────────────────────────────────────────────────────────

	/// <summary>
	/// Adds a task that runs before channel bootstrap (e.g., creating synonym sets or query rules).
	/// </summary>
	public ISyncOrchestrator<TEvent> AddPreBootstrapTask(
		Func<ITransport, CancellationToken, Task> task)
	{
		_preBootstrapTasks.Add(task);
		return this;
	}

	/// <summary>
	/// Creates channels, runs bootstrap, detects rollover, and resolves previous backing indices.
	/// Fast: does not reindex. Any needed rollover backfill is surfaced via
	/// <see cref="DeltaOrchestratorContext{TEvent}.PendingRolloverBackfills"/> and must be
	/// completed (via <see cref="BackfillRolledOverIndicesAsync"/>) before calling
	/// <see cref="TryWrite"/>.
	/// </summary>
	public async Task<ISyncOrchestratorContext> StartAsync(
		BootstrapMethod method, CancellationToken ctx = default)
	{
		foreach (var task in _preBootstrapTasks)
			await task(_transport, ctx).ConfigureAwait(false);

		_primaryWriteAlias = _primaryTypeContext.ResolveWriteAlias();
		_secondaryWriteAlias = _secondaryTypeContext.ResolveWriteAlias();

		// Capture current backing indices BEFORE bootstrap creates new ones (rollover detection).
		var prevPrimaryIndex = await ResolveExistingIndexIfAliasExistsAsync(_primaryWriteAlias, ctx).ConfigureAwait(false);
		var prevSecondaryIndex = await ResolveExistingIndexIfAliasExistsAsync(_secondaryWriteAlias, ctx).ConfigureAwait(false);

		// ── Primary channel ──────────────────────────────────────────────
		var reusePrimary = await TryResolveReusableIndexAsync(_primaryTypeContext, _primaryWriteAlias, ctx).ConfigureAwait(false);
		var primaryOpts = new IngestChannelOptions<TEvent>(
			_transport, _primaryTypeContext, _batchTimestamp, indexNameOverride: reusePrimary);
		ConfigurePrimary?.Invoke(primaryOpts);
		_primaryChannel = new IngestChannel<TEvent>(primaryOpts);

		if (_primaryTypeContext.GetContentHash != null
			&& _primaryChannel.Options.Strategy.DocumentIngest is TypeContextIndexIngestStrategy<TEvent> primaryStrategy)
		{
			primaryStrategy.HashInfoFactory = (_, fieldName, combinedHash) =>
				new HashedBulkUpdate(
					fieldName, combinedHash,
					$"ctx._source.{BatchIndexDateField} = params.{BatchIndexDateField}",
					new Dictionary<string, string> { [BatchIndexDateField] = _batchTimestamp.ToString("o") });
		}

		var primaryServerHash = await _primaryChannel.GetIndexTemplateHashAsync(ctx).ConfigureAwait(false) ?? string.Empty;
		await _primaryChannel.BootstrapElasticsearchAsync(method, ctx).ConfigureAwait(false);

		if (reusePrimary != null && primaryServerHash != _primaryChannel.ChannelHash)
		{
			_primaryChannel.Dispose();
			primaryOpts = new IngestChannelOptions<TEvent>(_transport, _primaryTypeContext, _batchTimestamp);
			ConfigurePrimary?.Invoke(primaryOpts);
			_primaryChannel = new IngestChannel<TEvent>(primaryOpts);
			await _primaryChannel.BootstrapElasticsearchAsync(method, ctx).ConfigureAwait(false);
			reusePrimary = null;
		}

		_primaryIndexName = _primaryChannel.IndexName;
		var primaryRolledOver = primaryServerHash != _primaryChannel.ChannelHash;
		if (primaryRolledOver) _strategy = IngestSyncStrategy.Multiplex;

		var primaryRolloverInfo = new IndexRolloverInfo("primary", _primaryChannel.ChannelHash, primaryServerHash, primaryRolledOver);
		OnRolloverDecision?.Invoke(primaryRolloverInfo);

		// ── Secondary channel ────────────────────────────────────────────
		var head = await _transport.HeadAsync(_secondaryWriteAlias, ctx).ConfigureAwait(false);
		var secondaryExists = head.ApiCallDetails.HttpStatusCode == 200;

		var secondaryTc = _secondaryTypeContext with { GetContentHash = null };
		var reuseSecondary = await TryResolveReusableIndexAsync(_secondaryTypeContext, _secondaryWriteAlias, ctx).ConfigureAwait(false);

		var secondaryOpts = new IngestChannelOptions<TEvent>(
			_transport, secondaryTc, _batchTimestamp, indexNameOverride: reuseSecondary);
		ConfigureSecondary?.Invoke(secondaryOpts);
		_secondaryChannel = new IngestChannel<TEvent>(secondaryOpts);

		_secondaryIndexName = _secondaryChannel.IndexName;
		var secondaryServerHash = await _secondaryChannel.GetIndexTemplateHashAsync(ctx).ConfigureAwait(false) ?? string.Empty;
		await _secondaryChannel.BootstrapElasticsearchAsync(method, ctx).ConfigureAwait(false);

		if (reuseSecondary != null && secondaryServerHash != _secondaryChannel.ChannelHash)
		{
			_secondaryChannel.Dispose();
			secondaryOpts = new IngestChannelOptions<TEvent>(_transport, secondaryTc, _batchTimestamp);
			ConfigureSecondary?.Invoke(secondaryOpts);
			_secondaryChannel = new IngestChannel<TEvent>(secondaryOpts);
			await _secondaryChannel.BootstrapElasticsearchAsync(method, ctx).ConfigureAwait(false);
			_secondaryIndexName = _secondaryChannel.IndexName;
			reuseSecondary = null;
		}

		var secondaryRolledOver = secondaryServerHash != _secondaryChannel.ChannelHash;

		if (secondaryExists && !secondaryRolledOver)
			_strategy = IngestSyncStrategy.Reindex;
		else if (primaryRolledOver || secondaryRolledOver)
			_strategy = IngestSyncStrategy.Multiplex;

		var secondaryRolloverInfo = new IndexRolloverInfo("secondary", _secondaryChannel.ChannelHash, secondaryServerHash, secondaryRolledOver);
		OnRolloverDecision?.Invoke(secondaryRolloverInfo);

		// ── Reindex cutoff for Reindex mode ──────────────────────────────
		if (_strategy == IngestSyncStrategy.Reindex)
		{
			if (secondaryRolledOver)
			{
				_secondaryReindexTarget = _secondaryIndexName;
				_reindexCutoff = DateTimeOffset.MinValue;
			}
			else
			{
				_secondaryReindexTarget = await ResolveExistingIndexAsync(_secondaryWriteAlias, ctx).ConfigureAwait(false);
				_reindexCutoff = await QueryMaxBatchDateAsync(_secondaryWriteAlias!, ctx).ConfigureAwait(false);
			}
		}

		// ── Detect rollover backfill needs and write-alias swaps ─────────
		// When bootstrap creates a new backing index, Elasticsearch adds the write alias
		// to the new index while the old index still carries it too (aliases can span
		// multiple indices). DeltaSyncOrchestrator has no delete-by-date sweep to
		// compensate, so we record a deferred atomic alias swap for CompleteAsync.
		_pendingRolloverBackfills = [];
		_writeAliasSwaps.Clear();

		if (primaryRolledOver && prevPrimaryIndex != null && prevPrimaryIndex != _primaryIndexName)
		{
			_pendingRolloverBackfills.Add(new RolloverBackfillTask("primary", prevPrimaryIndex, _primaryIndexName!));
			_writeAliasSwaps.Add((_primaryWriteAlias!, _primaryTypeContext.IndexStrategy!.WriteTarget!, _primaryIndexName!));
		}
		if (secondaryRolledOver && prevSecondaryIndex != null && prevSecondaryIndex != _secondaryIndexName)
		{
			_pendingRolloverBackfills.Add(new RolloverBackfillTask("secondary", prevSecondaryIndex, _secondaryIndexName!));
			_writeAliasSwaps.Add((_secondaryWriteAlias!, _secondaryTypeContext.IndexStrategy!.WriteTarget!, _secondaryIndexName!));
		}

		_context = new DeltaOrchestratorContext<TEvent>
		{
			Strategy = _strategy,
			BatchTimestamp = _batchTimestamp,
			PrimaryWriteAlias = _primaryWriteAlias!,
			SecondaryWriteAlias = _secondaryWriteAlias!,
			PrimaryReadAlias = _primaryTypeContext.ResolveReadTarget(),
			SecondaryReadAlias = _secondaryTypeContext.ResolveReadTarget(),
			PrimaryRollover = primaryRolloverInfo,
			SecondaryRollover = secondaryRolloverInfo,
			PendingRolloverBackfills = new List<RolloverBackfillTask>(_pendingRolloverBackfills),
		};
		return _context;
	}

	/// <summary>
	/// Reindexes each detected rollover backfill task (previous backing index → new backing index),
	/// yielding progress on each poll interval. Safe to call unconditionally — yields zero items
	/// immediately when no rollover was detected.
	/// <para>
	/// Must be called and awaited to completion before any <see cref="TryWrite"/> when
	/// <see cref="DeltaOrchestratorContext{TEvent}.PendingRolloverBackfills"/> is non-empty.
	/// </para>
	/// <para>
	/// Cancellable and resumable: if cancelled mid-reindex, the next <see cref="StartAsync"/>
	/// will re-detect the same pending backfills (alias has not swapped yet) and re-running this
	/// method resumes from where it left off (<c>_reindex</c> with the same destination is
	/// idempotent for unchanged source documents).
	/// </para>
	/// </summary>
	public async IAsyncEnumerable<RolloverBackfillProgress> BackfillRolledOverIndicesAsync(
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		while (_pendingRolloverBackfills.Count > 0)
		{
			var task = _pendingRolloverBackfills[0];
			var label = $"rollover-backfill-{task.Label}";
			var reindex = new ServerReindex(_transport, new ServerReindexOptions
			{
				Source = task.SourceIndex,
				Destination = task.DestinationIndex,
			});
			await foreach (var p in reindex.MonitorAsync(ct).ConfigureAwait(false))
			{
				var progress = new RolloverBackfillProgress
				{
					Label = task.Label,
					SourceIndex = task.SourceIndex,
					DestinationIndex = task.DestinationIndex,
					Total = p.Total,
					Processed = p.Created + p.Updated,
					Failed = p.VersionConflicts,
					Completed = p.IsCompleted,
				};
				OnReindexProgress?.Invoke(label, p);
				yield return progress;
			}

			// Backfill done: atomically move the write alias off the old index so the new
			// index is the sole backing target. Done here rather than in CompleteAsync so
			// the alias window where both old and new indices are live is as short as possible.
			var swap = _writeAliasSwaps.Find(s => s.NewIndex == task.DestinationIndex);
			if (swap != default)
				await SwapWriteAliasAsync(swap.WriteAlias, swap.WriteTarget, swap.NewIndex, ct).ConfigureAwait(false);

			_pendingRolloverBackfills.RemoveAt(0);
		}
	}

	/// <inheritdoc />
	public bool TryWrite(TEvent item)
	{
		EnsureStarted();
		EnsureNoBackfillPending();
		StampDocument(item);
		if (_strategy == IngestSyncStrategy.Multiplex)
			return _primaryChannel!.TryWrite(item) && _secondaryChannel!.TryWrite(item);
		return _primaryChannel!.TryWrite(item);
	}

	/// <inheritdoc />
	public Task<bool> WaitToWriteAsync(TEvent item, CancellationToken ctx = default)
	{
		EnsureStarted();
		EnsureNoBackfillPending();
		StampDocument(item);
		if (_strategy == IngestSyncStrategy.Multiplex)
			return WaitToWriteBothAsync(item, ctx);
		return _primaryChannel!.WaitToWriteAsync(item, ctx);
	}

	/// <inheritdoc />
	public bool TryWriteMany(IEnumerable<TEvent> events)
	{
		EnsureStarted();
		EnsureNoBackfillPending();
		var allWritten = true;
		foreach (var e in events)
		{
			if (!TryWrite(e)) allWritten = false;
		}
		return allWritten;
	}

	/// <inheritdoc />
	public async Task<bool> WaitToWriteManyAsync(IEnumerable<TEvent> events, CancellationToken ctx = default)
	{
		EnsureStarted();
		EnsureNoBackfillPending();
		var allWritten = true;
		foreach (var e in events)
		{
			if (!await WaitToWriteAsync(e, ctx).ConfigureAwait(false))
				allWritten = false;
		}
		return allWritten;
	}

	/// <summary>
	/// Drains both channels, runs reindex-updates (Reindex mode) or alias-apply (Multiplex mode),
	/// then fires the <see cref="OnPostComplete"/> hook.
	/// Unlike <see cref="IncrementalSyncOrchestrator{TEvent}"/>, this method never issues a
	/// <c>delete-by-query</c> over <c>batch_index_date</c>.
	/// </summary>
	public async Task<bool> CompleteAsync(TimeSpan? drainMaxWait = null, CancellationToken ctx = default)
	{
		EnsureStarted();

		await _primaryChannel!.WaitForDrainAsync(drainMaxWait, ctx).ConfigureAwait(false);
		await _primaryChannel.RefreshAsync(ctx).ConfigureAwait(false);
		await _primaryChannel.ApplyAliasesAsync(_primaryIndexName!, ctx).ConfigureAwait(false);

		if (_strategy == IngestSyncStrategy.Multiplex)
		{
			await _secondaryChannel!.WaitForDrainAsync(drainMaxWait, ctx).ConfigureAwait(false);
			await _secondaryChannel.RefreshAsync(ctx).ConfigureAwait(false);
			await _secondaryChannel.ApplyAliasesAsync(_secondaryIndexName!, ctx).ConfigureAwait(false);
		}
		else // Reindex — propagate changes via last_updated range query
		{
			var secondaryTarget = _secondaryReindexTarget;
			var secondaryHead = await _transport.HeadAsync(_secondaryWriteAlias!, ctx).ConfigureAwait(false);
			if (secondaryHead.ApiCallDetails.HttpStatusCode != 200)
			{
				await _secondaryChannel!.BootstrapElasticsearchAsync(BootstrapMethod.Failure, ctx).ConfigureAwait(false);
				secondaryTarget = _secondaryWriteAlias;
				await _transport.PutAsync<StringResponse>(secondaryTarget!, PostData.String("{}"), ctx).ConfigureAwait(false);
			}

			if (string.IsNullOrEmpty(secondaryTarget))
				secondaryTarget = _secondaryWriteAlias;

			// Reindex only new/changed docs (last_updated > reindexCutoff).
			// Strict "gt" so docs already reindexed in the previous batch are not re-sent —
			// re-reindexing would wipe AI-enriched fields on the secondary.
			await ReindexAsync("reindex-updates", _primaryWriteAlias!, secondaryTarget!,
				BuildRangeQuery(LastUpdatedField, "gt", _reindexCutoff), ctx).ConfigureAwait(false);

			await _primaryChannel.ApplyAliasesAsync(_primaryIndexName!, ctx).ConfigureAwait(false);
			await _secondaryChannel!.ApplyAliasesAsync(_secondaryIndexName!, ctx).ConfigureAwait(false);

			await _primaryChannel.RefreshAsync(ctx).ConfigureAwait(false);
			await _secondaryChannel.RefreshAsync(ctx).ConfigureAwait(false);
		}

		if (OnPostComplete != null && _context != null)
			await OnPostComplete(_context, _transport, ctx).ConfigureAwait(false);

		return true;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_primaryChannel?.Dispose();
		_secondaryChannel?.Dispose();
		GC.SuppressFinalize(this);
	}

	// ── Private helpers ──────────────────────────────────────────────────

	private static void EnsureHasBatchFields(object? setBatchIndexDate, object? setLastUpdated, string paramName)
	{
		if (setBatchIndexDate is null)
			throw new ArgumentException(
				"Document type lacks a [BatchIndexDate] property; " +
				"DeltaSyncOrchestrator requires it for the reindex-updates cutoff.",
				paramName);
		if (setLastUpdated is null)
			throw new ArgumentException(
				"Document type lacks a [LastUpdated] property; " +
				"DeltaSyncOrchestrator requires it for the reindex-updates cutoff.",
				paramName);
	}

	private void EnsureStarted()
	{
		if (_primaryChannel == null)
			throw new InvalidOperationException("Call StartAsync before writing or completing.");
	}

	private void EnsureNoBackfillPending()
	{
		if (_pendingRolloverBackfills.Count > 0)
			throw new InvalidOperationException(
				$"{_pendingRolloverBackfills.Count} rolled-over index(es) require backfill before deltas can be accepted. " +
				"Call (and await) BackfillRolledOverIndicesAsync() first, " +
				"or check DeltaOrchestratorContext.PendingRolloverBackfills returned by StartAsync.");
	}

	private void StampDocument(TEvent item)
	{
		_setBatchIndexDate?.Invoke(item, _batchTimestamp);
		_setLastUpdated?.Invoke(item, _batchTimestamp);
	}

	private async Task<bool> WaitToWriteBothAsync(TEvent item, CancellationToken ctx)
	{
		var primary = await _primaryChannel!.WaitToWriteAsync(item, ctx).ConfigureAwait(false);
		var secondary = await _secondaryChannel!.WaitToWriteAsync(item, ctx).ConfigureAwait(false);
		return primary && secondary;
	}

	private async Task<string?> ResolveExistingIndexIfAliasExistsAsync(string alias, CancellationToken ctx)
	{
		var head = await _transport.HeadAsync(alias, ctx).ConfigureAwait(false);
		if (head.ApiCallDetails.HttpStatusCode != 200)
			return null;
		return await ResolveExistingIndexAsync(alias, ctx).ConfigureAwait(false);
	}

	private async Task<string?> ResolveExistingIndexAsync(string alias, CancellationToken ctx)
	{
		var rq = new RequestConfiguration { Accept = "text/plain" };
		var response = await _transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"_cat/aliases/{alias}?h=index", null, rq, ctx
		).ConfigureAwait(false);
		var index = response.Body?.Trim(Environment.NewLine.ToCharArray());
		return string.IsNullOrEmpty(index) ? null : index;
	}

	private async Task<string?> TryResolveReusableIndexAsync(
		ElasticsearchTypeContext tc, string writeAlias, CancellationToken ctx)
	{
		if (tc.IndexStrategy?.DatePattern == null)
			return null;

		var aliasHead = await _transport.HeadAsync(writeAlias, ctx).ConfigureAwait(false);
		if (aliasHead.ApiCallDetails.HttpStatusCode != 200)
			return null;

		var templateName = $"{tc.IndexStrategy.WriteTarget}-template";
		var hashResponse = await _transport.RequestAsync<StringResponse>(
			HttpMethod.GET,
			$"/_index_template/{templateName}?filter_path=index_templates.index_template._meta.hash",
			cancellationToken: ctx).ConfigureAwait(false);

		if (!hashResponse.ApiCallDetails.HasSuccessfulStatusCode || string.IsNullOrEmpty(hashResponse.Body))
			return null;

		return await ResolveExistingIndexAsync(writeAlias, ctx).ConfigureAwait(false);
	}

	private async Task<DateTimeOffset> QueryMaxBatchDateAsync(string index, CancellationToken ctx)
	{
		var body = $$"""
		{
			"size": 0,
			"aggs": { "max_batch": { "max": { "field": "{{BatchIndexDateField}}" } } }
		}
		""";
		var response = await _transport.RequestAsync<StringResponse>(
			HttpMethod.POST, $"{index}/_search", PostData.String(body), cancellationToken: ctx
		).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			return DateTimeOffset.MinValue;

		using var doc = JsonDocument.Parse(response.Body);
		if (doc.RootElement.TryGetProperty("aggregations", out var aggs)
			&& aggs.TryGetProperty("max_batch", out var maxBatch)
			&& maxBatch.TryGetProperty("value_as_string", out var valueStr))
		{
			if (DateTimeOffset.TryParse(valueStr.GetString(), out var parsed))
				return parsed;
		}

		return DateTimeOffset.MinValue;
	}

	private async Task ReindexAsync(string label, string source, string dest, string query, CancellationToken ctx)
	{
		var reindex = new ServerReindex(_transport, new ServerReindexOptions
		{
			Source = source,
			Destination = dest,
			Query = query,
		});
		await foreach (var progress in reindex.MonitorAsync(ctx).ConfigureAwait(false))
			OnReindexProgress?.Invoke(label, progress);
	}

	private string BuildRangeQuery(string field, string op, DateTimeOffset? timestamp = null) =>
		$$"""{ "range": { "{{field}}": { "{{op}}": "{{timestamp ?? _batchTimestamp:o}}" } } }""";

	/// <summary>
	/// Atomically removes <paramref name="writeAlias"/> from all <c>{writeTarget}-*</c> indices
	/// and re-adds it exclusively to <paramref name="newIndex"/>. This corrects the state left
	/// by bootstrap, which adds the alias to the new index without removing it from the old one.
	/// </summary>
	private async Task SwapWriteAliasAsync(string writeAlias, string writeTarget, string newIndex, CancellationToken ctx)
	{
		var pattern = $"{writeTarget}-*";
		var body = $$"""
		{
			"actions": [
				{ "remove": { "index": "{{pattern}}", "alias": "{{writeAlias}}", "must_exist": false } },
				{ "add":    { "index": "{{newIndex}}", "alias": "{{writeAlias}}" } }
			]
		}
		""";
		await _transport.RequestAsync<StringResponse>(
			HttpMethod.POST, "_aliases", PostData.String(body), cancellationToken: ctx
		).ConfigureAwait(false);
	}
}
