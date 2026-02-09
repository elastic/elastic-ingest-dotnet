// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Ingest.Elasticsearch.Serialization;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Ingest strategy for data streams. Always uses CreateOperation (append-only).
/// </summary>
public class DataStreamIngestStrategy<TDocument> : IDocumentIngestStrategy<TDocument>
{
	private readonly CreateOperation _fixedHeader = new();
	private readonly string _dataStreamName;
	private readonly string _url;

	/// <summary>
	/// Creates a new data stream ingest strategy.
	/// </summary>
	/// <param name="dataStreamName">The data stream name (e.g., "logs-nginx.access-production").</param>
	/// <param name="baseBulkPathAndQuery">The base bulk path and query string.</param>
	public DataStreamIngestStrategy(string dataStreamName, string baseBulkPathAndQuery)
	{
		_dataStreamName = dataStreamName;
		_url = $"{dataStreamName}/{baseBulkPathAndQuery}";
	}

	/// <inheritdoc />
	public BulkOperationHeader CreateBulkOperationHeader(TDocument document, string channelHash) => _fixedHeader;

	/// <inheritdoc />
	public string GetBulkUrl(string baseBulkPathAndQuery) => _url;

	/// <inheritdoc />
	public string RefreshTargets => _dataStreamName;
}
