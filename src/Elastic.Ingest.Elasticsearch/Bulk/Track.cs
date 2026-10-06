// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;

namespace Elastic.Ingest.Elasticsearch.Bulk;

/// <summary>
/// Which identity fields of each item a <c>_bulk</c> response should report: <see cref="Serialization.BulkResponseItem.Id"/>
/// and/or <see cref="Serialization.BulkResponseItem.Index"/>. Combine them with <c>|</c>.
/// <para>They are the only way to learn a server generated id or the concrete index behind an alias or data stream.
/// Neither is reported by default: they make responses bigger and cost an allocation per item, and many workloads
/// (for example logs written to a data stream) do not need them.</para>
/// </summary>
[Flags]
public enum Track
{
	/// <summary>Report neither. <c>Id</c> and <c>Index</c> stay <c>null</c>.</summary>
	None = 0,

	/// <summary>Report the <c>_id</c> of each item: the id Elasticsearch generated, or the explicit id echoed back.</summary>
	Id = 1,

	/// <summary>Report the concrete <c>_index</c> that received each item, for example the backing index behind an alias or data stream.</summary>
	Index = 2
}
