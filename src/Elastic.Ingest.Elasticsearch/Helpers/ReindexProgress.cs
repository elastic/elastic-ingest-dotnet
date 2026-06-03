// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;

namespace Elastic.Ingest.Elasticsearch.Helpers;

/// <summary>
/// Progress snapshot for a server-side _reindex operation.
/// </summary>
public class ReindexProgress
{
	/// <summary> The Elasticsearch task ID. Stable across node-shutdown relocations when using the reindex management API. </summary>
	public string TaskId { get; init; } = string.Empty;

	/// <summary> Whether the reindex task has completed. </summary>
	public bool IsCompleted { get; init; }

	/// <summary> Whether the reindex task has been cancelled. </summary>
	public bool Cancelled { get; init; }

	/// <summary> Total documents to process. </summary>
	public long Total { get; init; }

	/// <summary> Documents created. </summary>
	public long Created { get; init; }

	/// <summary> Documents updated. </summary>
	public long Updated { get; init; }

	/// <summary> Documents deleted. </summary>
	public long Deleted { get; init; }

	/// <summary> Documents that were no-ops. </summary>
	public long Noops { get; init; }

	/// <summary> Version conflicts encountered. </summary>
	public long VersionConflicts { get; init; }

	/// <summary> Time elapsed since the task started. </summary>
	public TimeSpan Elapsed { get; init; }

	/// <summary> Fraction of work completed (0.0 to 1.0), or null if total is unknown. </summary>
	public double? FractionComplete => Total > 0 ? (double)(Created + Updated + Deleted + Noops) / Total : null;

	/// <summary> A sanitized description of the reindex operation (source/dest indices, remote host). Only populated by the reindex management API. </summary>
	public string? Description { get; init; }

	/// <summary> When the task started. Only populated by the reindex management API. </summary>
	public DateTimeOffset? StartTime { get; init; }

	/// <summary> Error description if the task failed. </summary>
	public string? Error { get; init; }
}
