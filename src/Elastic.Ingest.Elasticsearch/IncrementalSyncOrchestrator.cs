// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Channels.Diagnostics;
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
	public required IngestStrategy Strategy { get; init; }

	/// <summary> The batch timestamp used for range queries during completion. </summary>
	public required DateTimeOffset BatchTimestamp { get; init; }
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

	private ElasticsearchChannel<TEvent>? _primaryChannel;
	private ElasticsearchChannel<TEvent>? _secondaryChannel;
	private IngestStrategy _strategy = IngestStrategy.Reindex;

	private string? _primaryWriteAlias;
	private string? _secondaryWriteAlias;
	private string? _secondaryReindexTarget;

	/// <summary>
	/// Creates the orchestrator from two <see cref="ElasticsearchTypeContext"/> instances.
	/// Channels are created internally during <see cref="StartAsync"/>.
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
	public Action<ElasticsearchChannelOptions<TEvent>>? ConfigurePrimary { get; set; }

	/// <summary> Optional configuration callback for the secondary channel options. </summary>
	public Action<ElasticsearchChannelOptions<TEvent>>? ConfigureSecondary { get; set; }

	/// <summary> Optional hook that runs after <see cref="CompleteAsync"/> finishes all operations. </summary>
	public Func<OrchestratorContext<TEvent>, CancellationToken, Task>? OnPostComplete { get; set; }

	/// <summary> The resolved ingest strategy after <see cref="StartAsync"/> completes. </summary>
	public IngestStrategy Strategy => _strategy;

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
	public async Task<IngestStrategy> StartAsync(
		BootstrapMethod method, string? ilmPolicy = null, CancellationToken ctx = default)
	{
		// 1. Run pre-bootstrap tasks
		foreach (var task in _preBootstrapTasks)
			await task(_transport, ctx).ConfigureAwait(false);

		// 2. Create and bootstrap primary channel
		var primaryOpts = new ElasticsearchChannelOptions<TEvent>(_transport, _primaryTypeContext);
		ConfigurePrimary?.Invoke(primaryOpts);
		_primaryChannel = new ElasticsearchChannel<TEvent>(primaryOpts);

		var primaryServerHash = await _primaryChannel.GetIndexTemplateHashAsync(ctx).ConfigureAwait(false) ?? string.Empty;
		await _primaryChannel.BootstrapElasticsearchAsync(method, ilmPolicy, ctx).ConfigureAwait(false);

		_primaryWriteAlias = ResolvePrimaryWriteAlias();
		if (primaryServerHash != _primaryChannel.ChannelHash)
			_strategy = IngestStrategy.Multiplex;

		// 3. Check secondary alias existence
		_secondaryWriteAlias = ResolveSecondaryWriteAlias();
		var head = await _transport.HeadAsync(_secondaryWriteAlias, ctx).ConfigureAwait(false);
		if (head.ApiCallDetails.HttpStatusCode != 200)
			_strategy = IngestStrategy.Multiplex;

		// 4. Create and bootstrap secondary channel
		var secondaryOpts = new ElasticsearchChannelOptions<TEvent>(_transport, _secondaryTypeContext);
		ConfigureSecondary?.Invoke(secondaryOpts);
		_secondaryChannel = new ElasticsearchChannel<TEvent>(secondaryOpts);

		var secondaryServerHash = await _secondaryChannel.GetIndexTemplateHashAsync(ctx).ConfigureAwait(false) ?? string.Empty;
		await _secondaryChannel.BootstrapElasticsearchAsync(method, ilmPolicy, ctx).ConfigureAwait(false);

		if (secondaryServerHash != _secondaryChannel.ChannelHash)
			_strategy = IngestStrategy.Multiplex;

		// 5. Resolve reindex target for Reindex mode
		if (_strategy == IngestStrategy.Reindex)
			_secondaryReindexTarget = await ResolveExistingIndexAsync(_secondaryWriteAlias, ctx).ConfigureAwait(false);

		return _strategy;
	}

	/// <inheritdoc />
	public bool TryWrite(TEvent item)
	{
		EnsureStarted();
		if (_strategy == IngestStrategy.Multiplex)
			return _primaryChannel!.TryWrite(item) && _secondaryChannel!.TryWrite(item);
		return _primaryChannel!.TryWrite(item);
	}

	/// <inheritdoc />
	public Task<bool> WaitToWriteAsync(TEvent item, CancellationToken ctx = default)
	{
		EnsureStarted();
		if (_strategy == IngestStrategy.Multiplex)
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
		await _primaryChannel.ApplyAliasesAsync(string.Empty, ctx).ConfigureAwait(false);

		if (_strategy == IngestStrategy.Multiplex)
		{
			// 2a. Drain + refresh + alias secondary
			await _secondaryChannel!.WaitForDrainAsync(drainMaxWait, ctx).ConfigureAwait(false);
			await _secondaryChannel.RefreshAsync(ctx).ConfigureAwait(false);
			await _secondaryChannel.ApplyAliasesAsync(string.Empty, ctx).ConfigureAwait(false);

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
				await _secondaryChannel!.BootstrapElasticsearchAsync(BootstrapMethod.Failure, null, ctx).ConfigureAwait(false);
				// Create the target index with empty body to trigger template
				secondaryTarget = _secondaryWriteAlias;
				await _transport.PutAsync<StringResponse>(secondaryTarget!, PostData.String("{}"), ctx).ConfigureAwait(false);
			}

			if (string.IsNullOrEmpty(secondaryTarget))
				secondaryTarget = _secondaryWriteAlias;

			// 3. Reindex updates: last_updated >= batchTimestamp
			await ReindexAsync(_primaryWriteAlias!, secondaryTarget!, "updates",
				BuildRangeQuery(LastUpdatedField, "gte"), ctx).ConfigureAwait(false);

			// 4. Reindex deletions: batch_index_date < batchTimestamp → delete script
			await ReindexWithDeleteScriptAsync(_primaryWriteAlias!, secondaryTarget!, "deletions",
				BuildRangeQuery(BatchIndexDateField, "lt"), ctx).ConfigureAwait(false);

			// 5. Delete old from primary
			await DeleteOldDocumentsAsync(_primaryWriteAlias!, ctx).ConfigureAwait(false);

			// 6. Apply aliases
			await _primaryChannel.ApplyAliasesAsync(string.Empty, ctx).ConfigureAwait(false);
			await _secondaryChannel!.ApplyAliasesAsync(string.Empty, ctx).ConfigureAwait(false);

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
				BatchTimestamp = _batchTimestamp
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
		if (_strategy == IngestStrategy.Multiplex)
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
		if (!await _primaryChannel!.ApplyAliasesAsync(string.Empty, ctx).ConfigureAwait(false))
			return false;
		return await _secondaryChannel!.ApplyAliasesAsync(string.Empty, ctx).ConfigureAwait(false);
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

	private string ResolvePrimaryWriteAlias()
	{
		var writeTarget = _primaryTypeContext.IndexStrategy?.WriteTarget;
		if (string.IsNullOrEmpty(writeTarget))
			throw new InvalidOperationException("Primary TypeContext must have IndexStrategy.WriteTarget");

		return _primaryTypeContext.IndexStrategy?.DatePattern != null
			? $"{writeTarget}-latest"
			: writeTarget!;
	}

	private string ResolveSecondaryWriteAlias()
	{
		var writeTarget = _secondaryTypeContext.IndexStrategy?.WriteTarget;
		if (string.IsNullOrEmpty(writeTarget))
			throw new InvalidOperationException("Secondary TypeContext must have IndexStrategy.WriteTarget");

		return _secondaryTypeContext.IndexStrategy?.DatePattern != null
			? $"{writeTarget}-latest"
			: writeTarget!;
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

	private async Task ReindexAsync(string source, string dest, string opType, string query, CancellationToken ctx)
	{
		var body = $$"""
		{
			"source": { "index": "{{source}}", "query": {{query}} },
			"dest": { "index": "{{dest}}" }
		}
		""";
		var response = await _transport.PostAsync<StringResponse>(
			"_reindex?wait_for_completion=false", PostData.String(body), ctx
		).ConfigureAwait(false);

		var taskId = ExtractTaskId(response.Body);
		if (taskId != null)
			await PollTaskAsync(taskId, $"reindex-{opType}", ctx).ConfigureAwait(false);
	}

	private async Task ReindexWithDeleteScriptAsync(string source, string dest, string opType, string query, CancellationToken ctx)
	{
		var body = $$"""
		{
			"source": { "index": "{{source}}", "query": {{query}} },
			"dest": { "index": "{{dest}}", "op_type": "index" },
			"script": { "source": "ctx.op = 'delete'", "lang": "painless" }
		}
		""";
		var response = await _transport.PostAsync<StringResponse>(
			"_reindex?wait_for_completion=false", PostData.String(body), ctx
		).ConfigureAwait(false);

		var taskId = ExtractTaskId(response.Body);
		if (taskId != null)
			await PollTaskAsync(taskId, $"reindex-{opType}", ctx).ConfigureAwait(false);
	}

	private async Task DeleteOldDocumentsAsync(string alias, CancellationToken ctx)
	{
		var query = BuildRangeQuery(BatchIndexDateField, "lt");
		var body = $$"""{ "query": {{query}} }""";
		var response = await _transport.PostAsync<StringResponse>(
			$"{alias}/_delete_by_query?wait_for_completion=false", PostData.String(body), ctx
		).ConfigureAwait(false);

		var taskId = ExtractTaskId(response.Body);
		if (taskId != null)
			await PollTaskAsync(taskId, "delete-by-query", ctx).ConfigureAwait(false);
	}

	private async Task PollTaskAsync(string taskId, string operation, CancellationToken ctx)
	{
		while (!ctx.IsCancellationRequested)
		{
			await Task.Delay(TimeSpan.FromSeconds(5), ctx).ConfigureAwait(false);

			var response = await _transport.GetAsync<StringResponse>($"_tasks/{taskId}", ctx).ConfigureAwait(false);
			if (!response.ApiCallDetails.HasSuccessfulStatusCode)
				break;

			if (response.Body?.Contains("\"completed\":true") == true
				|| response.Body?.Contains("\"completed\": true") == true)
				break;
		}
	}

	private string BuildRangeQuery(string field, string op) =>
		$$"""{ "range": { "{{field}}": { "{{op}}": "{{_batchTimestamp:o}}" } } }""";

	private static string? ExtractTaskId(string? body)
	{
		if (string.IsNullOrEmpty(body))
			return null;

		// Parse {"task":"nodeId:taskNumber"} pattern
		var taskMarker = "\"task\":\"";
		var start = body!.IndexOf(taskMarker, StringComparison.Ordinal);
		if (start < 0)
			return null;

		start += taskMarker.Length;
		var end = body.IndexOf('"', start);
		return end > start ? body.Substring(start, end - start) : null;
	}

	private void EnsureStarted()
	{
		if (_primaryChannel == null)
			throw new InvalidOperationException("Call StartAsync before writing or completing.");
	}
}
