// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Ingest.Elasticsearch.Bulk;

/// <summary>
/// Whether a <c>_bulk</c> response reports the <c>_id</c> and <c>_index</c> of each item
/// (<see cref="Serialization.BulkResponseItem.Id"/> and <see cref="Serialization.BulkResponseItem.Index"/>).
/// <para>They are the only way to learn a server generated id or the concrete index behind an alias or data stream.
/// Reporting them makes responses bigger and costs an allocation per item, so by default they are only requested when they carry information the caller does not have.</para>
/// </summary>
public enum BulkItemIdentity
{
	/// <summary>
	/// Request them for a request that contains an <c>index</c> or <c>create</c> action without an id (Elasticsearch generates the id)
	/// or an action with <see cref="BulkAction.WithRequireAlias"/> (the concrete index is not known). Requests where every
	/// action carries its id and targets a concrete index do not pay for them.
	/// <para>A <c>Target</c> or <c>index</c> that is an alias or a data stream is not detected: use <see cref="Always"/> to learn the backing index.</para>
	/// </summary>
	Auto = 0,

	/// <summary>Always request them.</summary>
	Always,

	/// <summary>Never request them: <c>Id</c> and <c>Index</c> stay <c>null</c>.</summary>
	Never
}
