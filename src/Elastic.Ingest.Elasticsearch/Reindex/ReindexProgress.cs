// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;

namespace Elastic.Ingest.Elasticsearch.Reindex;

/// <summary>
/// Progress snapshot for a server-side _reindex operation.
/// </summary>
public class ReindexProgress
{
	/// <summary> The Elasticsearch task ID. </summary>
	public string TaskId { get; init; } = string.Empty;

	/// <summary> Whether the reindex task has completed. </summary>
	public bool IsCompleted { get; init; }

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

	/// <summary> Error description if the task failed. </summary>
	public string? Error { get; init; }
}
