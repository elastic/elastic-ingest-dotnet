// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Ingest.Elasticsearch.Serialization;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Per-document strategy for determining bulk operation type, target, and URL.
/// </summary>
public interface IDocumentIngestStrategy<in TDocument>
{
	/// <summary>
	/// Creates the bulk operation header for a given document.
	/// </summary>
	BulkOperationHeader CreateBulkOperationHeader(TDocument document, string channelHash);

	/// <summary>
	/// Gets the bulk URL path and query string, potentially prepending a target.
	/// </summary>
	string GetBulkUrl(string baseBulkPathAndQuery);

	/// <summary>
	/// Gets the indices/data streams to refresh after writing.
	/// </summary>
	string RefreshTargets { get; }
}
