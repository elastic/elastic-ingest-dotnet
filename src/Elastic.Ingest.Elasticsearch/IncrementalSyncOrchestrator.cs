// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
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
/// Context returned by <see cref="IncrementalSyncOrchestrator{TEvent}.StartAsync"/> and passed
/// to <see cref="IncrementalSyncOrchestrator{TEvent}.OnPostComplete"/> hooks.
/// </summary>
public class OrchestratorContext<TEvent> where TEvent : class
{
	/// <summary> The resolved ingest strategy (Reindex or Multiplex). </summary>
	public IngestSyncStrategy Strategy { get; init; }

	/// <summary> The batch timestamp used for range queries during completion. </summary>
	public DateTimeOffset BatchTimestamp { get; init; }

	/// <summary> The resolved primary write alias (or data stream name). </summary>
	public string PrimaryWriteAlias { get; init; } = null!;

	/// <summary> The resolved secondary write alias (or data stream name). </summary>
	public string? SecondaryWriteAlias { get; init; }

	/// <summary> The resolved primary read target (ReadAlias or fallback to write alias). </summary>
	public string PrimaryReadAlias { get; init; } = null!;

	/// <summary> The resolved secondary read target (ReadAlias or fallback to write alias). </summary>
	public string? SecondaryReadAlias { get; init; }
}

/// <summary>
/// Orchestrates two Elasticsearch channels (primary + secondary) sharing the same document type
/// for incremental sync workflows. Always uses Reindex (write to primary, reindex changed docs to
/// secondary) when the secondary index already exists, because the secondary may contain
/// semantic_text fields that reject scripted bulk upserts. Multiplex (write to both) is only
/// used when creating entirely new backing indices.
/// </summary>
public class IncrementalSyncOrchestrator<TEvent> : IBufferedChannel<TEvent>, IDisposable
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
	private IngestSyncStrategy _strategy = IngestSyncStrategy.Reindex;

	private string? _primaryWriteAlias;
	private string? _secondaryWriteAlias;
	private string? _primaryIndexName;
	private string? _secondaryIndexName;
	private string? _secondaryReindexTarget;
	private DateTimeOffset _reindexCutoff;
	private OrchestratorContext<TEvent>? _context;

	/// <summary>
	/// Creates the orchestrator from two <see cref="IStaticMappingResolver{T}"/> instances
	/// (one per index target). Each resolver carries its own <see cref="ElasticsearchTypeContext"/>
	/// with fully configured <see cref="IndexStrategy"/> and <see cref="SearchStrategy"/>.
	/// Batch tracking field names and setter delegates are resolved from the primary resolver.
	/// </summary>
	public IncrementalSyncOrchestrator(
		ITransport transport,
		IStaticMappingResolver<TEvent> primary,
		IStaticMappingResolver<TEvent> secondary)
	{
		_transport = transport;
		_setBatchIndexDate = primary.SetBatchIndexDate;
		_setLastUpdated = primary.SetLastUpdated;

		BatchIndexDateField = primary.BatchIndexDateFieldName ?? "batch_index_date";
		LastUpdatedField = primary.LastUpdatedFieldName ?? "last_updated";

		_primaryTypeContext = primary.Context with { IndexPatternUseBatchDate = true };
		_secondaryTypeContext = secondary.Context with { IndexPatternUseBatchDate = true };
	}

	/// <summary>
	/// Creates the orchestrator from two raw <see cref="ElasticsearchTypeContext"/> instances
	/// with optional delegate params for document stamping. Use this when you don't have
	/// source-generated resolvers.
	/// </summary>
	public IncrementalSyncOrchestrator(
		ITransport transport,
		ElasticsearchTypeContext primary,
		ElasticsearchTypeContext secondary,
		Action<TEvent, DateTimeOffset>? setBatchIndexDate = null,
		Action<TEvent, DateTimeOffset>? setLastUpdated = null,
		string? batchIndexDateField = null,
		string? lastUpdatedField = null)
	{
		_transport = transport;
		_primaryTypeContext = primary;
		_secondaryTypeContext = secondary;
		_setBatchIndexDate = setBatchIndexDate;
		_setLastUpdated = setLastUpdated;
		if (batchIndexDateField != null) BatchIndexDateField = batchIndexDateField;
		if (lastUpdatedField != null) LastUpdatedField = lastUpdatedField;
	}

	/// <summary> The field name used for last-updated range queries. </summary>
	public string LastUpdatedField { get; init; } = "last_updated";

	/// <summary> The field name used for batch-index-date range queries. </summary>
	public string BatchIndexDateField { get; init; } = "batch_index_date";

	/// <summary> Optional configuration callback for the primary channel options. </summary>
	public Action<IngestChannelOptions<TEvent>>? ConfigurePrimary { get; init; }

	/// <summary> Optional configuration callback for the secondary channel options. </summary>
	public Action<IngestChannelOptions<TEvent>>? ConfigureSecondary { get; init; }

	/// <summary> Optional hook that runs after <see cref="CompleteAsync"/> finishes all operations. </summary>
	public Func<OrchestratorContext<TEvent>, ITransport, CancellationToken, Task>? OnPostComplete { get; init; }

	/// <summary> The resolved ingest strategy after <see cref="StartAsync"/> completes. </summary>
	public IngestSyncStrategy Strategy => _strategy;

	/// <summary> The batch timestamp assigned when the orchestrator was created. </summary>
	public DateTimeOffset BatchTimestamp => _batchTimestamp;

	/// <summary>
	/// Adds a task that runs before channel bootstrap (e.g., creating synonym sets or query rules).
	/// </summary>
	public IncrementalSyncOrchestrator<TEvent> AddPreBootstrapTask(
		Func<ITransport, CancellationToken, Task> task)
	{
		_preBootstrapTasks.Add(task);
		return this;
	}

	/// <summary>
	/// Creates channels, runs bootstrap, and determines the ingest strategy.
	/// Returns the orchestrator context with resolved aliases and strategy.
	/// Multiplex (write to both) is only used when creating new backing indices —
	/// if the secondary already exists, Reindex is always used because the secondary
	/// may contain semantic_text fields that reject scripted bulk upserts.
	/// </summary>
	public async Task<OrchestratorContext<TEvent>> StartAsync(BootstrapMethod method, CancellationToken ctx = default)
	{
		// 1. Run pre-bootstrap tasks
		foreach (var task in _preBootstrapTasks)
			await task(_transport, ctx).ConfigureAwait(false);

		// 2. Create and bootstrap primary channel.
		var primaryOpts = new IngestChannelOptions<TEvent>(_transport, _primaryTypeContext, _batchTimestamp);
		ConfigurePrimary?.Invoke(primaryOpts);
		_primaryChannel = new IngestChannel<TEvent>(primaryOpts);

		// When the primary uses content-hash scripted upserts, inject a hash-match factory
		// that updates batch_index_date instead of NOOPing. This ensures unchanged documents
		// are still marked as part of the current batch so the cleanup step doesn't delete them.
		if (_primaryTypeContext.GetContentHash != null
		    && _primaryChannel.Options.Strategy.DocumentIngest is TypeContextIndexIngestStrategy<TEvent> primaryStrategy)
		{
			primaryStrategy.HashInfoFactory = (_, fieldName, combinedHash) =>
				new HashedBulkUpdate(
					fieldName,
					combinedHash,
					$"ctx._source.{BatchIndexDateField} = params.{BatchIndexDateField}",
					new Dictionary<string, string>
					{
						[BatchIndexDateField] = _batchTimestamp.ToString("o")
					});
		}

		var primaryServerHash = await _primaryChannel.GetIndexTemplateHashAsync(ctx).ConfigureAwait(false) ?? string.Empty;
		await _primaryChannel.BootstrapElasticsearchAsync(method, ctx).ConfigureAwait(false);

		_primaryWriteAlias = ResolvePrimaryWriteAlias();
		_primaryIndexName = _primaryChannel.IndexName;
		if (primaryServerHash != _primaryChannel.ChannelHash)
			_strategy = IngestSyncStrategy.Multiplex;

		// 3. Check secondary alias existence
		_secondaryWriteAlias = ResolveSecondaryWriteAlias();
		var head = await _transport.HeadAsync(_secondaryWriteAlias, ctx).ConfigureAwait(false);
		var secondaryExists = head.ApiCallDetails.HttpStatusCode == 200;

		// 4. Create and bootstrap secondary channel.
		// Strip ContentHash from the secondary — the secondary is populated via _reindex,
		// never via scripted bulk upserts (which fail on semantic_text fields).
		var secondaryTc = _secondaryTypeContext with { GetContentHash = null };
		var secondaryOpts = new IngestChannelOptions<TEvent>(_transport, secondaryTc, _batchTimestamp);
		ConfigureSecondary?.Invoke(secondaryOpts);
		_secondaryChannel = new IngestChannel<TEvent>(secondaryOpts);

		_secondaryIndexName = _secondaryChannel.IndexName;
		var secondaryServerHash = await _secondaryChannel.GetIndexTemplateHashAsync(ctx).ConfigureAwait(false) ?? string.Empty;
		await _secondaryChannel.BootstrapElasticsearchAsync(method, ctx).ConfigureAwait(false);

		if (secondaryServerHash != _secondaryChannel.ChannelHash)
			_strategy = IngestSyncStrategy.Multiplex;

		// 5. If the secondary index already exists we must use Reindex — Multiplex
		// would write directly to the secondary via scripted bulk upserts, which
		// fails on indices containing semantic_text fields.
		if (secondaryExists)
			_strategy = IngestSyncStrategy.Reindex;

		// 6. Resolve reindex target and cutoff for Reindex mode.
		// Query the secondary for max(batch_index_date) so we pick up any changes
		// since the last successful sync — including docs from prior failed syncs.
		if (_strategy == IngestSyncStrategy.Reindex)
		{
			_secondaryReindexTarget = await ResolveExistingIndexAsync(_secondaryWriteAlias, ctx).ConfigureAwait(false);
			_reindexCutoff = await QueryMaxBatchDateAsync(_secondaryWriteAlias!, ctx).ConfigureAwait(false);
		}

		_context = BuildContext();
		return _context;
	}

	/// <inheritdoc />
	public bool TryWrite(TEvent item)
	{
		EnsureStarted();
		StampDocument(item);
		if (_strategy == IngestSyncStrategy.Multiplex)
			return _primaryChannel!.TryWrite(item) && _secondaryChannel!.TryWrite(item);
		return _primaryChannel!.TryWrite(item);
	}

	/// <inheritdoc />
	public Task<bool> WaitToWriteAsync(TEvent item, CancellationToken ctx = default)
	{
		EnsureStarted();
		StampDocument(item);
		if (_strategy == IngestSyncStrategy.Multiplex)
			return WaitToWriteBothAsync(item, ctx);
		return _primaryChannel!.WaitToWriteAsync(item, ctx);
	}

	/// <inheritdoc />
	public bool TryWriteMany(IEnumerable<TEvent> events)
	{
		EnsureStarted();
		var allWritten = true;
		foreach (var e in events)
		{
			if (!TryWrite(e))
				allWritten = false;
		}
		return allWritten;
	}

	/// <inheritdoc />
	public async Task<bool> WaitToWriteManyAsync(IEnumerable<TEvent> events, CancellationToken ctx = default)
	{
		EnsureStarted();
		var allWritten = true;
		foreach (var e in events)
		{
			if (!await WaitToWriteAsync(e, ctx).ConfigureAwait(false))
				allWritten = false;
		}
		return allWritten;
	}

	/// <inheritdoc />
	public IChannelDiagnosticsListener? DiagnosticsListener => null;

	/// <summary>
	/// Drains both channels, then performs reindex/delete-by-query (Reindex mode) or
	/// delete-old + alias (Multiplex mode), then runs the post-complete hook.
	/// </summary>
	public async Task<bool> CompleteAsync(TimeSpan? drainMaxWait = null, CancellationToken ctx = default)
	{
		EnsureStarted();

		// 1. Drain + refresh + alias primary
		await _primaryChannel!.WaitForDrainAsync(drainMaxWait, ctx).ConfigureAwait(false);
		await _primaryChannel.RefreshAsync(ctx).ConfigureAwait(false);
		await _primaryChannel.ApplyAliasesAsync(_primaryIndexName!, ctx).ConfigureAwait(false);

		if (_strategy == IngestSyncStrategy.Multiplex)
		{
			// 2a. Drain + refresh + alias secondary
			await _secondaryChannel!.WaitForDrainAsync(drainMaxWait, ctx).ConfigureAwait(false);
			await _secondaryChannel.RefreshAsync(ctx).ConfigureAwait(false);
			await _secondaryChannel.ApplyAliasesAsync(_secondaryIndexName!, ctx).ConfigureAwait(false);

			// 3. Delete old from primary
			await DeleteOldDocumentsAsync(_primaryWriteAlias!, ctx).ConfigureAwait(false);
			await _primaryChannel.RefreshAsync(ctx).ConfigureAwait(false);
		}
		else // Reindex strategy
		{
			// 2b. Ensure secondary target exists
			var secondaryTarget = _secondaryReindexTarget;
			var secondaryHead = await _transport.HeadAsync(_secondaryWriteAlias!, ctx).ConfigureAwait(false);
			if (secondaryHead.ApiCallDetails.HttpStatusCode != 200)
			{
				await _secondaryChannel!.BootstrapElasticsearchAsync(BootstrapMethod.Failure, ctx).ConfigureAwait(false);
				// Create the target index with empty body to trigger template
				secondaryTarget = _secondaryWriteAlias;
				await _transport.PutAsync<StringResponse>(secondaryTarget!, PostData.String("{}"), ctx).ConfigureAwait(false);
			}

			if (string.IsNullOrEmpty(secondaryTarget))
				secondaryTarget = _secondaryWriteAlias;

			// 3. Reindex updates: last_updated >= reindexCutoff (max batch_index_date from secondary).
			// Uses the secondary's last known sync timestamp so failed prior syncs are retried.
			await ReindexAsync(_primaryWriteAlias!, secondaryTarget!,
				BuildRangeQuery(LastUpdatedField, "gte", _reindexCutoff), ctx).ConfigureAwait(false);

			// 4. Reindex deletions: batch_index_date < batchTimestamp → delete script
			await ReindexWithDeleteScriptAsync(_primaryWriteAlias!, secondaryTarget!,
				BuildRangeQuery(BatchIndexDateField, "lt"), ctx).ConfigureAwait(false);

			// 5. Delete old from primary
			await DeleteOldDocumentsAsync(_primaryWriteAlias!, ctx).ConfigureAwait(false);

			// 6. Apply aliases
			await _primaryChannel.ApplyAliasesAsync(_primaryIndexName!, ctx).ConfigureAwait(false);
			await _secondaryChannel!.ApplyAliasesAsync(_secondaryIndexName!, ctx).ConfigureAwait(false);

			// 7. Refresh both
			await _primaryChannel.RefreshAsync(ctx).ConfigureAwait(false);
			await _secondaryChannel.RefreshAsync(ctx).ConfigureAwait(false);
		}

		// 8. Post-complete hook
		if (OnPostComplete != null && _context != null)
			await OnPostComplete(_context, _transport, ctx).ConfigureAwait(false);

		return true;
	}

	/// <summary> Drain all registered channels, waiting for pending data to be flushed. </summary>
	public async Task DrainAllAsync(TimeSpan? maxWait = null, CancellationToken ctx = default)
	{
		EnsureStarted();
		await _primaryChannel!.WaitForDrainAsync(maxWait, ctx).ConfigureAwait(false);
		if (_strategy == IngestSyncStrategy.Multiplex)
			await _secondaryChannel!.WaitForDrainAsync(maxWait, ctx).ConfigureAwait(false);
	}

	/// <summary> Refresh all registered channels' targets. </summary>
	public async Task<bool> RefreshAllAsync(CancellationToken ctx = default)
	{
		EnsureStarted();
		if (!await _primaryChannel!.RefreshAsync(ctx).ConfigureAwait(false))
			return false;
		return await _secondaryChannel!.RefreshAsync(ctx).ConfigureAwait(false);
	}

	/// <summary> Apply aliases for all registered channels. </summary>
	public async Task<bool> ApplyAllAliasesAsync(CancellationToken ctx = default)
	{
		EnsureStarted();
		if (!await _primaryChannel!.ApplyAliasesAsync(_primaryIndexName!, ctx).ConfigureAwait(false))
			return false;
		return await _secondaryChannel!.ApplyAliasesAsync(_secondaryIndexName!, ctx).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_primaryChannel?.Dispose();
		_secondaryChannel?.Dispose();
		GC.SuppressFinalize(this);
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

	private OrchestratorContext<TEvent> BuildContext() =>
		new()
		{
			Strategy = _strategy,
			BatchTimestamp = _batchTimestamp,
			PrimaryWriteAlias = _primaryWriteAlias!,
			SecondaryWriteAlias = _secondaryWriteAlias,
			PrimaryReadAlias = _primaryTypeContext.ResolveReadTarget(),
			SecondaryReadAlias = _secondaryTypeContext.ResolveReadTarget(),
		};

	private string ResolvePrimaryWriteAlias() =>
		_primaryTypeContext.ResolveWriteAlias();

	private string ResolveSecondaryWriteAlias() =>
		_secondaryTypeContext.ResolveWriteAlias();

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

	private async Task<string?> ResolveExistingIndexAsync(string alias, CancellationToken ctx)
	{
		var rq = new RequestConfiguration { Accept = "text/plain" };
		var response = await _transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"_cat/aliases/{alias}?h=index", null, rq, ctx
		).ConfigureAwait(false);
		var index = response.Body?.Trim(Environment.NewLine.ToCharArray());
		return string.IsNullOrEmpty(index) ? null : index;
	}

	private async Task ReindexAsync(string source, string dest, string query, CancellationToken ctx)
	{
		var reindex = new ServerReindex(_transport, new ServerReindexOptions
		{
			Source = source,
			Destination = dest,
			Query = query,
		});
		await reindex.RunAsync(ctx).ConfigureAwait(false);
	}

	private async Task ReindexWithDeleteScriptAsync(string source, string dest, string query, CancellationToken ctx)
	{
		var body = $$"""
		{
			"source": { "index": "{{source}}", "query": {{query}} },
			"dest": { "index": "{{dest}}", "op_type": "index" },
			"script": { "source": "ctx.op = 'delete'", "lang": "painless" }
		}
		""";
		var reindex = new ServerReindex(_transport, new ServerReindexOptions
		{
			Source = source,
			Destination = dest,
			Body = body,
		});
		await reindex.RunAsync(ctx).ConfigureAwait(false);
	}

	private async Task DeleteOldDocumentsAsync(string alias, CancellationToken ctx)
	{
		var query = BuildRangeQuery(BatchIndexDateField, "lt");
		var dbq = new DeleteByQuery(_transport, new DeleteByQueryOptions
		{
			Index = alias,
			QueryBody = query,
		});
		await dbq.RunAsync(ctx).ConfigureAwait(false);
	}

	private string BuildRangeQuery(string field, string op, DateTimeOffset? timestamp = null) =>
		$$"""{ "range": { "{{field}}": { "{{op}}": "{{timestamp ?? _batchTimestamp:o}}" } } }""";

	private void EnsureStarted()
	{
		if (_primaryChannel == null)
			throw new InvalidOperationException("Call StartAsync before writing or completing.");
	}
}
