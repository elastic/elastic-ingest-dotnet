// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using static System.Globalization.CultureInfo;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Ingest strategy for configurable indices with optional date rolling, ID lookup, and upsert support.
/// </summary>
public class IndexIngestStrategy<TDocument> : IDocumentIngestStrategy<TDocument>
{
	private readonly IndexChannelOptions<TDocument> _options;
	private readonly bool _skipIndexNameOnOperations;
	private readonly string _url;
	private readonly string _refreshTargets;

	/// <summary>
	/// Creates a new index ingest strategy from IndexChannelOptions.
	/// </summary>
	public IndexIngestStrategy(IndexChannelOptions<TDocument> options, string baseBulkPathAndQuery)
	{
		_options = options;
		_url = baseBulkPathAndQuery;

		// When the configured index format represents a fixed index name, optimize
		if (string.Format(InvariantCulture, options.IndexFormat, DateTimeOffset.UtcNow)
			.Equals(options.IndexFormat, StringComparison.Ordinal))
		{
			_url = $"{options.IndexFormat}/{baseBulkPathAndQuery}";
			_skipIndexNameOnOperations = true;
		}

		_refreshTargets = _skipIndexNameOnOperations
			? options.IndexFormat
			: string.Format(InvariantCulture, options.IndexFormat, "*");
	}

	/// <inheritdoc />
	public BulkOperationHeader CreateBulkOperationHeader(TDocument document, string channelHash) =>
		BulkRequestDataFactory.CreateBulkOperationHeaderForIndex(document, channelHash, _options, _skipIndexNameOnOperations);

	/// <inheritdoc />
	public string GetBulkUrl(string baseBulkPathAndQuery) => _url;

	/// <inheritdoc />
	public string RefreshTargets => _refreshTargets;
}
