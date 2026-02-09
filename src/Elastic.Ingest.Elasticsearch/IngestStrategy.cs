// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// Determines how the <see cref="IncrementalSyncOrchestrator{TEvent}"/> synchronizes
/// the secondary index with the primary.
/// </summary>
public enum IngestStrategy
{
	/// <summary>
	/// Primary receives writes; secondary is updated via _reindex after drain.
	/// Used when template hashes match (no schema changes).
	/// </summary>
	Reindex,

	/// <summary>
	/// Both primary and secondary receive writes directly.
	/// Used when template hashes differ or secondary index doesn't exist.
	/// </summary>
	Multiplex
}
