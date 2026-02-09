// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Ingest.Elasticsearch.Serialization;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Ingest strategy for wired streams (managed by Elasticsearch).
/// Sends to logs/_bulk with standard Bulk API NDJSON format using CreateOperation.
/// </summary>
public class WiredStreamIngestStrategy<TDocument> : IDocumentIngestStrategy<TDocument>
{
	private readonly CreateOperation _fixedHeader = new();
	private readonly string _url;

	/// <summary>
	/// Creates a new wired stream ingest strategy.
	/// </summary>
	/// <param name="baseBulkPathAndQuery">The base bulk path and query string.</param>
	public WiredStreamIngestStrategy(string baseBulkPathAndQuery)
	{
		_url = $"logs/{baseBulkPathAndQuery}";
	}

	/// <inheritdoc />
	public BulkOperationHeader CreateBulkOperationHeader(TDocument document, string channelHash) => _fixedHeader;

	/// <inheritdoc />
	public string GetBulkUrl(string baseBulkPathAndQuery) => _url;

	/// <inheritdoc />
	public string RefreshTargets => "logs";
}
