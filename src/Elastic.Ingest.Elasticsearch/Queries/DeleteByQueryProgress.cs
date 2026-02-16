// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;

namespace Elastic.Ingest.Elasticsearch.Queries;

/// <summary>
/// Progress snapshot for a _delete_by_query operation.
/// </summary>
public class DeleteByQueryProgress
{
	/// <summary> The Elasticsearch task ID. </summary>
	public string TaskId { get; init; } = string.Empty;

	/// <summary> Whether the delete task has completed. </summary>
	public bool IsCompleted { get; init; }

	/// <summary> Documents deleted. </summary>
	public long Deleted { get; init; }

	/// <summary> Total documents to process. </summary>
	public long Total { get; init; }

	/// <summary> Version conflicts encountered. </summary>
	public long VersionConflicts { get; init; }

	/// <summary> Time elapsed since the task started. </summary>
	public TimeSpan Elapsed { get; init; }

	/// <summary> Fraction of work completed (0.0 to 1.0), or null if total is unknown. </summary>
	public double? FractionComplete => Total > 0 ? (double)Deleted / Total : null;

	/// <summary> Error description if the task failed. </summary>
	public string? Error { get; init; }
}
