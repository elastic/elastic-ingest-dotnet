// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System.Collections.Generic;

namespace Elastic.Ingest.Elasticsearch.Search;

/// <summary>
/// Represents a page of search results from a point-in-time search.
/// </summary>
public class SearchPage<TDocument>
{
	/// <summary> The documents in this page. </summary>
	public IReadOnlyList<TDocument> Documents { get; init; } = [];

	/// <summary> The total number of matching documents (from hits.total.value). </summary>
	public long TotalDocuments { get; init; }

	/// <summary> Whether there are more pages to fetch. </summary>
	public bool HasMore { get; init; }
}
