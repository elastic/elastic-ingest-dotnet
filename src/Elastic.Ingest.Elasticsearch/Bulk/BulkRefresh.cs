// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Ingest.Elasticsearch.Bulk;

/// <summary>
/// The <c>refresh</c> parameter of a <c>_bulk</c> request: when the written documents become visible to search.
/// <para>When a <see cref="BulkSender{TItem,TBody}"/> does not set one, Elasticsearch applies its default and refreshes on the index's own interval.</para>
/// </summary>
public enum BulkRefresh
{
	/// <summary>Do not refresh as part of the request (<c>refresh=false</c>).</summary>
	False = 0,

	/// <summary>Refresh the affected shards immediately after the request, before it returns (<c>refresh=true</c>). This costs a refresh per request.</summary>
	True,

	/// <summary>Return only after the changes became visible to search, without forcing a refresh (<c>refresh=wait_for</c>).</summary>
	WaitFor
}
