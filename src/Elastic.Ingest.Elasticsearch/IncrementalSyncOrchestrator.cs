// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Channels.Diagnostics;
using Elastic.Ingest.Elasticsearch.Helpers;
using Elastic.Ingest.Elasticsearch.Strategies;
using Elastic.Mapping;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// Context provided to <see cref="IncrementalSyncOrchestrator{TEvent}.OnPostComplete"/> hooks.
/// </summary>
public class OrchestratorContext<TEvent> where TEvent : class
{
	/// <summary> The transport used by the orchestrator. </summary>
	public required ITransport Transport { get; init; }

	/// <summary> The resolved ingest strategy (Reindex or Multiplex). </summary>
	public required IngestSyncStrategy Strategy { get; init; }

	/// <summary> The batch timestamp used for range queries during completion. </summary>
	public required DateTimeOffset BatchTimestamp { get; init; }

	/// <summary> The resolved primary write alias (or data stream name). </summary>
	public required string PrimaryWriteAlias { get; init; }

	/// <summary> The resolved secondary write alias (or data stream name). </summary>
	public required string? SecondaryWriteAlias { get; init; }

	/// <summary> The resolved primary read target (ReadAlias or fallback to write alias). </summary>
	public required string PrimaryReadAlias { get; init; }

	/// <summary> The resolved secondary read target (ReadAlias or fallback to write alias). </summary>
	public required string? SecondaryReadAlias { get; init; }
}

/// <summary>
/// Orchestrates two Elasticsearch channels (primary + secondary) sharing the same document type
/// for incremental sync workflows. Automatically determines whether to use Multiplex (write to both)
/// or Reindex (write to primary, reindex to secondary) based on template hash comparison.
/// </summary>
public class IncrementalSyncOrchestrator<TEvent> : IBufferedChannel<TEvent>, IDisposable
	where TEvent : class
{
	private readonly ITransport _transport;
	private readonly ElasticsearchTypeContext _primaryTypeContext;
	private readonly ElasticsearchTypeContext _secondaryTypeContext;
	private readonly DateTimeOffset _batchTimestamp = DateTimeOffset.UtcNow;
	private readonly List<Func<ITransport, CancellationToken, Task>> _preBootstrapTasks = new();

	private IngestChannel<TEvent>? _primaryChannel;
	private IngestChannel<TEvent>? _secondaryChannel;
	private IngestSyncStrategy _strategy = IngestSyncStrategy.Reindex;

	private string? _primaryWriteAlias;
	private string? _secondaryWriteAlias;
	private string? _primaryIndexName;
	private string? _secondaryIndexName;
	private string? _secondaryReindexTarget;

	/// <summary>
	/// Creates the orchestrator from two <see cref="ElasticsearchTypeContext"/> instances.
	/// Strategies are auto-resolved from the type contexts.
	/// </summary>
	public IncrementalSyncOrchestrator(
		ITransport transport,
		ElasticsearchTypeContext primary,
		ElasticsearchTypeContext secondary)
	{
		_transport = transport;
		_primaryTypeContext = primary;
		_secondaryTypeContext = secondary;
	}

	/// <summary> The field name used for last-updated range queries. </summary>
	public string LastUpdatedField { get; set; } = "last_updated";

	/// <summary> The field name used for batch-index-date range queries. </summary>
	public string BatchIndexDateField { get; set; } = "batch_index_date";

	/// <summary> Optional configuration callback for the primary channel options. </summary>
	public Action<IngestChannelOptions<TEvent>>? ConfigurePrimary { get; set; }

	/// <summary> Optional configuration callback for the secondary channel options. </summary>
	public Action<IngestChannelOptions<TEvent>>? ConfigureSecondary { get; set; }

	/// <summary> Optional hook that runs after <see cref="CompleteAsync"/> finishes all operations. </summary>
	public Func<OrchestratorContext<TEvent>, CancellationToken, Task>? OnPostComplete { get; set; }

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
	/// Creates channels, runs bootstrap, and determines the ingest strategy by comparing
	/// template hashes and checking secondary alias existence.
	/// </summary>
	public async Task<IngestSyncStrategy> StartAsync(BootstrapMethod method, CancellationToken ctx = default)
	{
		// 1. Run pre-bootstrap tasks
		foreach (var task in _preBootstrapTasks)
			await task(_transport, ctx).ConfigureAwait(false);

		// 2. Create and bootstrap primary channel
		var primaryOpts = new IngestChannelOptions<TEvent>(_transport, _primaryTypeContext, _batchTimestamp);
		ConfigurePrimary?.Invoke(primaryOpts);
		_primaryChannel = new IngestChannel<TEvent>(primaryOpts);

		var primaryServerHash = await _primaryChannel.GetIndexTemplateHashAsync(ctx).ConfigureAwait(false) ?? string.Empty;
		await _primaryChannel.BootstrapElasticsearchAsync(method, ctx).ConfigureAwait(false);

		_primaryWriteAlias = ResolvePrimaryWriteAlias();
		_primaryIndexName = _primaryChannel.IndexName;
		if (primaryServerHash != _primaryChannel.ChannelHash)
			_strategy = IngestSyncStrategy.Multiplex;

		// 3. Check secondary alias existence
		_secondaryWriteAlias = ResolveSecondaryWriteAlias();
		var head = await _transport.HeadAsync(_secondaryWriteAlias, ctx).ConfigureAwait(false);
		if (head.ApiCallDetails.HttpStatusCode != 200)
			_strategy = IngestSyncStrategy.Multiplex;

		// 4. Create and bootstrap secondary channel
		var secondaryOpts = new IngestChannelOptions<TEvent>(_transport, _secondaryTypeContext, _batchTimestamp);
		ConfigureSecondary?.Invoke(secondaryOpts);
		_secondaryChannel = new IngestChannel<TEvent>(secondaryOpts);

		_secondaryIndexName = _secondaryChannel.IndexName;
		var secondaryServerHash = await _secondaryChannel.GetIndexTemplateHashAsync(ctx).ConfigureAwait(false) ?? string.Empty;
		await _secondaryChannel.BootstrapElasticsearchAsync(method, ctx).ConfigureAwait(false);

		if (secondaryServerHash != _secondaryChannel.ChannelHash)
			_strategy = IngestSyncStrategy.Multiplex;

		// 5. Resolve reindex target for Reindex mode
		if (_strategy == IngestSyncStrategy.Reindex)
			_secondaryReindexTarget = await ResolveExistingIndexAsync(_secondaryWriteAlias, ctx).ConfigureAwait(false);

		return _strategy;
	}

	/// <inheritdoc />
	public bool TryWrite(TEvent item)
	{
		EnsureStarted();
		if (_strategy == IngestSyncStrategy.Multiplex)
			return _primaryChannel!.TryWrite(item) && _secondaryChannel!.TryWrite(item);
		return _primaryChannel!.TryWrite(item);
	}

	/// <inheritdoc />
	public Task<bool> WaitToWriteAsync(TEvent item, CancellationToken ctx = default)
	{
		EnsureStarted();
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

			// 3. Reindex updates: last_updated >= batchTimestamp
			await ReindexAsync(_primaryWriteAlias!, secondaryTarget!,
				BuildRangeQuery(LastUpdatedField, "gte"), ctx).ConfigureAwait(false);

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
		if (OnPostComplete != null)
		{
			var context = new OrchestratorContext<TEvent>
			{
				Transport = _transport,
				Strategy = _strategy,
				BatchTimestamp = _batchTimestamp,
				PrimaryWriteAlias = _primaryWriteAlias!,
				SecondaryWriteAlias = _secondaryWriteAlias,
				PrimaryReadAlias = TypeContextResolver.ResolveReadTarget(_primaryTypeContext),
				SecondaryReadAlias = TypeContextResolver.ResolveReadTarget(_secondaryTypeContext),
			};
			await OnPostComplete(context, ctx).ConfigureAwait(false);
		}

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

	private async Task<bool> WaitToWriteBothAsync(TEvent item, CancellationToken ctx)
	{
		var primary = await _primaryChannel!.WaitToWriteAsync(item, ctx).ConfigureAwait(false);
		var secondary = await _secondaryChannel!.WaitToWriteAsync(item, ctx).ConfigureAwait(false);
		return primary && secondary;
	}

	private string ResolvePrimaryWriteAlias() =>
		TypeContextResolver.ResolveWriteAlias(_primaryTypeContext);

	private string ResolveSecondaryWriteAlias() =>
		TypeContextResolver.ResolveWriteAlias(_secondaryTypeContext);

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

	private string BuildRangeQuery(string field, string op) =>
		$$"""{ "range": { "{{field}}": { "{{op}}": "{{_batchTimestamp:o}}" } } }""";

	private void EnsureStarted()
	{
		if (_primaryChannel == null)
			throw new InvalidOperationException("Call StartAsync before writing or completing.");
	}
}
