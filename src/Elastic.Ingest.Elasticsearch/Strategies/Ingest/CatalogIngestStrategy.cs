// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Ingest strategy for catalog (fixed single-index) use cases.
/// Always skips index name on operations since the URL contains the target.
/// </summary>
public class CatalogIngestStrategy<TDocument> : IDocumentIngestStrategy<TDocument>
{
	private readonly IndexChannelOptions<TDocument> _options;
	private string _indexName;
	private string _url;

	/// <summary>
	/// Creates a new catalog ingest strategy.
	/// </summary>
	/// <param name="indexName">The fixed index name to write to.</param>
	/// <param name="baseBulkPathAndQuery">The base bulk path and query string.</param>
	/// <param name="options">The index channel options for ID/upsert lookups.</param>
	public CatalogIngestStrategy(string indexName, string baseBulkPathAndQuery, IndexChannelOptions<TDocument> options)
	{
		_indexName = indexName;
		_url = $"{indexName}/{baseBulkPathAndQuery}";
		_options = options;
	}

	/// <summary>
	/// Updates the target index name (e.g. when reusing an existing index).
	/// </summary>
	public void UpdateIndexName(string indexName, string baseBulkPathAndQuery)
	{
		_indexName = indexName;
		_url = $"{indexName}/{baseBulkPathAndQuery}";
	}

	/// <summary> The current index name being written to. </summary>
	public string IndexName => _indexName;

	/// <inheritdoc />
	public BulkOperationHeader CreateBulkOperationHeader(TDocument document, string channelHash) =>
		BulkRequestDataFactory.CreateBulkOperationHeaderForIndex(document, channelHash, _options, skipIndexName: true);

	/// <inheritdoc />
	public string GetBulkUrl(string baseBulkPathAndQuery) => _url;

	/// <inheritdoc />
	public string RefreshTargets => _indexName;
}
