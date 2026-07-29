// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Mapping;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Ingest strategy for wired streams (managed by Elasticsearch).
/// Sends to a wired bulk endpoint (<c>logs/_bulk</c>, <c>logs.ecs/_bulk</c>, or
/// <c>logs.otel/_bulk</c>) using CreateOperation.
/// </summary>
public class WiredStreamIngestStrategy<TDocument> : IDocumentIngestStrategy<TDocument>
{
	private readonly CreateOperation _fixedHeader = new();
	private readonly string _bulkPathPrefix;
	private readonly string _url;

	/// <summary>
	/// Creates a new wired stream ingest strategy targeting the legacy <c>logs</c> endpoint.
	/// </summary>
	/// <param name="baseBulkPathAndQuery">The base bulk path and query string.</param>
	public WiredStreamIngestStrategy(string baseBulkPathAndQuery)
		: this(baseBulkPathAndQuery, WiredStreamIngestEndpoint.Logs)
	{
	}

	/// <summary>
	/// Creates a new wired stream ingest strategy for the given endpoint.
	/// </summary>
	/// <param name="baseBulkPathAndQuery">The base bulk path and query string.</param>
	/// <param name="endpoint">The wired stream ingest endpoint.</param>
	public WiredStreamIngestStrategy(string baseBulkPathAndQuery, WiredStreamIngestEndpoint endpoint)
		: this(baseBulkPathAndQuery, endpoint.ToBulkPathPrefix())
	{
	}

	/// <summary>
	/// Creates a new wired stream ingest strategy with an explicit bulk path prefix.
	/// </summary>
	/// <param name="baseBulkPathAndQuery">The base bulk path and query string.</param>
	/// <param name="bulkPathPrefix">The URL path prefix (e.g. <c>logs.ecs</c>).</param>
	public WiredStreamIngestStrategy(string baseBulkPathAndQuery, string bulkPathPrefix)
	{
		_bulkPathPrefix = bulkPathPrefix;
		_url = $"{bulkPathPrefix}/{baseBulkPathAndQuery}";
	}

	/// <inheritdoc />
	public BulkOperationHeader CreateBulkOperationHeader(TDocument document, string channelHash) => _fixedHeader;

	/// <inheritdoc />
	public string GetBulkUrl(string baseBulkPathAndQuery) => _url;

	/// <inheritdoc />
	public string RefreshTargets => _bulkPathPrefix;
}
