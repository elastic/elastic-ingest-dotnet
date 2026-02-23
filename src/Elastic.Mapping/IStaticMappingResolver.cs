// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Source-generated resolver that provides an <see cref="ElasticsearchTypeContext"/> together with
/// AOT-safe setter delegates for batch tracking fields. Used by IncrementalSyncOrchestrator to
/// automatically stamp documents and derive field names for range queries.
/// </summary>
/// <typeparam name="T">The document type this resolver is generated for.</typeparam>
public interface IStaticMappingResolver<T> where T : class
{
	/// <summary>The generated Elasticsearch type context (mappings, settings, hashes, accessors).</summary>
	ElasticsearchTypeContext Context { get; }

	/// <summary>
	/// AOT-safe setter for the <see cref="BatchIndexDateAttribute"/> property.
	/// Null when the document type has no such property.
	/// </summary>
	Action<T, DateTimeOffset>? SetBatchIndexDate { get; }

	/// <summary>
	/// AOT-safe setter for the <see cref="LastUpdatedAttribute"/> property.
	/// Null when the document type has no such property.
	/// </summary>
	Action<T, DateTimeOffset>? SetLastUpdated { get; }

	/// <summary>
	/// The Elasticsearch field name for the <see cref="BatchIndexDateAttribute"/> property,
	/// resolved from <c>[JsonPropertyName]</c> or naming policy. Null when the type has no such property.
	/// </summary>
	string? BatchIndexDateFieldName { get; }

	/// <summary>
	/// The Elasticsearch field name for the <see cref="LastUpdatedAttribute"/> property,
	/// resolved from <c>[JsonPropertyName]</c> or naming policy. Null when the type has no such property.
	/// </summary>
	string? LastUpdatedFieldName { get; }
}
