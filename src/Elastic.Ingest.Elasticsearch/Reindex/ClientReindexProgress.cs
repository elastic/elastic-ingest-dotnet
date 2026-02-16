// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;

namespace Elastic.Ingest.Elasticsearch.Reindex;

/// <summary>
/// Progress snapshot for a client-side reindex operation.
/// </summary>
public class ClientReindexProgress
{
	/// <summary> Number of documents read from the source. </summary>
	public long DocumentsRead { get; init; }

	/// <summary> Number of documents written to the destination. </summary>
	public long DocumentsWritten { get; init; }

	/// <summary> Whether the reindex has completed. </summary>
	public bool IsCompleted { get; init; }

	/// <summary> Time elapsed since the operation started. </summary>
	public TimeSpan Elapsed { get; init; }
}
