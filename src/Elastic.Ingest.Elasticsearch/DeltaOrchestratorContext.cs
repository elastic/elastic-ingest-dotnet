// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>Describes a single rollover backfill task that was detected by <see cref="DeltaSyncOrchestrator{TEvent}.StartAsync"/>.</summary>
/// <param name="Label">Identifies which channel rolled — <c>"primary"</c> or <c>"secondary"</c>.</param>
/// <param name="SourceIndex">The previous concrete backing index (to copy from).</param>
/// <param name="DestinationIndex">The new concrete backing index (to copy into).</param>
public record RolloverBackfillTask(string Label, string SourceIndex, string DestinationIndex);

/// <summary>
/// Progress snapshot yielded by <see cref="DeltaSyncOrchestrator{TEvent}.BackfillRolledOverIndicesAsync"/>.
/// </summary>
public class RolloverBackfillProgress
{
	/// <inheritdoc cref="RolloverBackfillTask.Label"/>
	public string Label { get; init; } = null!;

	/// <inheritdoc cref="RolloverBackfillTask.SourceIndex"/>
	public string SourceIndex { get; init; } = null!;

	/// <inheritdoc cref="RolloverBackfillTask.DestinationIndex"/>
	public string DestinationIndex { get; init; } = null!;

	/// <summary>Total number of documents to reindex.</summary>
	public long Total { get; init; }

	/// <summary>Number of documents processed so far (created + updated).</summary>
	public long Processed { get; init; }

	/// <summary>Number of version conflicts encountered.</summary>
	public long Failed { get; init; }

	/// <summary>Whether this backfill task has completed.</summary>
	public bool Completed { get; init; }
}

/// <summary>
/// Context returned by <see cref="DeltaSyncOrchestrator{TEvent}.StartAsync"/> and passed
/// to <see cref="DeltaSyncOrchestrator{TEvent}.OnPostComplete"/> hooks.
/// </summary>
public class DeltaOrchestratorContext<TEvent> : ISyncOrchestratorContext where TEvent : class
{
	/// <summary>The resolved ingest strategy (Reindex or Multiplex). Diagnostic; auto-resolved.</summary>
	public IngestSyncStrategy Strategy { get; init; }

	/// <summary>The batch timestamp assigned when the orchestrator was created.</summary>
	public DateTimeOffset BatchTimestamp { get; init; }

	/// <summary>The resolved primary write alias.</summary>
	public string PrimaryWriteAlias { get; init; } = null!;

	/// <summary>The resolved secondary write alias.</summary>
	public string SecondaryWriteAlias { get; init; } = null!;

	/// <summary>The resolved primary read target (read alias or fallback to write alias).</summary>
	public string PrimaryReadAlias { get; init; } = null!;

	/// <summary>The resolved secondary read target (read alias or fallback to write alias).</summary>
	public string SecondaryReadAlias { get; init; } = null!;

	/// <summary>Rollover decision details for the primary index.</summary>
	public IndexRolloverInfo? PrimaryRollover { get; init; }

	/// <summary>Rollover decision details for the secondary index.</summary>
	public IndexRolloverInfo? SecondaryRollover { get; init; }

	/// <summary>
	/// Rollover backfill tasks detected during <see cref="DeltaSyncOrchestrator{TEvent}.StartAsync"/>.
	/// Surfaced for telemetry. Callers do not need to inspect this before calling
	/// <see cref="DeltaSyncOrchestrator{TEvent}.BackfillRolledOverIndicesAsync"/> — that method
	/// is safe to call unconditionally and completes immediately when this list is empty.
	/// </summary>
	public IReadOnlyList<RolloverBackfillTask> PendingRolloverBackfills { get; init; } = [];
}
