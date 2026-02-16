// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;

namespace Elastic.Ingest.Elasticsearch.Helpers;

/// <summary>
/// Configuration for <see cref="ClientReindex{TDocument}"/>.
/// </summary>
public class ClientReindexOptions<TDocument> where TDocument : class
{
	/// <summary> PIT search options for reading from the source index. </summary>
	public required PointInTimeSearchOptions Source { get; init; }

	/// <summary> The destination channel for writing documents. The caller owns the channel lifecycle. </summary>
	public required IngestChannel<TDocument> Destination { get; init; }

	/// <summary> Optional document transform function applied before writing. </summary>
	public Func<TDocument, TDocument>? Transform { get; init; }
}
