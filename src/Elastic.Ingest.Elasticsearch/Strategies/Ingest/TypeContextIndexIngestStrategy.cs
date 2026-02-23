// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Mapping;
using static System.Globalization.CultureInfo;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Ingest strategy for index targets driven by <see cref="ElasticsearchTypeContext"/>.
/// Uses the TypeContext's generated accessor delegates for ID, content hash, and timestamp.
/// </summary>
public class TypeContextIndexIngestStrategy<TDocument> : IDocumentIngestStrategy<TDocument>
{
	private readonly ElasticsearchTypeContext _typeContext;
	private readonly bool _skipIndexNameOnOperations;
	private readonly string _url;
	private readonly string _refreshTargets;
	private readonly string _indexFormat;
	private Func<object, string, string, HashedBulkUpdate>? _hashInfoFactory;

	/// <summary>
	/// Optional factory that overrides how <see cref="HashedBulkUpdate"/> is created for
	/// scripted hash upserts. Parameters: (document, fieldName, combinedHash).
	/// Used by <see cref="IncrementalSyncOrchestrator{TEvent}"/> to inject a custom
	/// hash-match script that updates batch tracking fields instead of NOOPing.
	/// </summary>
	public Func<object, string, string, HashedBulkUpdate>? HashInfoFactory
	{
		get => _hashInfoFactory;
		set => _hashInfoFactory = value;
	}

	/// <summary>
	/// Creates a new TypeContext-driven index ingest strategy.
	/// </summary>
	/// <param name="typeContext">The Elasticsearch type context with generated accessors.</param>
	/// <param name="indexFormat">Format string for index name (e.g., "products-{0:yyyy.MM.dd}").</param>
	/// <param name="baseBulkPathAndQuery">The base bulk path and query string.</param>
	public TypeContextIndexIngestStrategy(ElasticsearchTypeContext typeContext, string indexFormat, string baseBulkPathAndQuery)
	{
		_typeContext = typeContext;
		_indexFormat = indexFormat;
		_url = baseBulkPathAndQuery;

		// When the configured index format represents a fixed index name, optimize
		if (string.Format(InvariantCulture, indexFormat, DateTimeOffset.UtcNow)
			.Equals(indexFormat, StringComparison.Ordinal))
		{
			_url = $"{indexFormat}/{baseBulkPathAndQuery}";
			_skipIndexNameOnOperations = true;
		}

		_refreshTargets = _skipIndexNameOnOperations
			? indexFormat
			: string.Format(InvariantCulture, indexFormat, "*");
	}

	/// <inheritdoc />
	public BulkOperationHeader CreateBulkOperationHeader(TDocument document, string channelHash)
	{
		var indexTime = _typeContext.GetTimestamp != null
			? _typeContext.GetTimestamp(document!) ?? DateTimeOffset.Now
			: DateTimeOffset.Now;

		var index = _skipIndexNameOnOperations
			? string.Empty
			: string.Format(InvariantCulture, _indexFormat, indexTime);

		var id = _typeContext.GetId?.Invoke(document!);

		// Content hash-based scripted upsert.
		// Combines channelHash (derived from template settings/mappings) with the document's
		// content hash so that mapping or settings changes invalidate all content hashes,
		// forcing documents to be re-indexed even when their content hasn't changed.
		if (!string.IsNullOrWhiteSpace(channelHash) && id != null && _typeContext.GetContentHash is not null)
		{
			var contentHash = _typeContext.GetContentHash(document!);
			if (contentHash != null)
			{
				var combinedHash = HashedBulkUpdate.CreateHash(channelHash, contentHash);
				var hashInfo = _hashInfoFactory != null
					? _hashInfoFactory(document!, _typeContext.ContentHashFieldName ?? "content_hash", combinedHash)
					: new HashedBulkUpdate(
						_typeContext.ContentHashFieldName ?? "content_hash", combinedHash);
				return _skipIndexNameOnOperations
					? new ScriptedHashUpdateOperation { Id = id, UpdateInformation = hashInfo }
					: new ScriptedHashUpdateOperation { Id = id, Index = index, UpdateInformation = hashInfo };
			}
		}

		// Has ID → IndexOperation
		if (!string.IsNullOrWhiteSpace(id))
			return _skipIndexNameOnOperations
				? new IndexOperation { Id = id }
				: new IndexOperation { Index = index, Id = id };

		// No ID → CreateOperation
		return _skipIndexNameOnOperations
			? new CreateOperation()
			: new CreateOperation { Index = index };
	}

	/// <inheritdoc />
	public string GetBulkUrl(string baseBulkPathAndQuery) => _url;

	/// <inheritdoc />
	public string RefreshTargets => _refreshTargets;
}
