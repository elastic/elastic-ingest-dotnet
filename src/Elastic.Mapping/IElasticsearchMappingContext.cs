// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Provides access to type metadata from a generated mapping context.
/// </summary>
public interface IElasticsearchMappingContext
{
	/// <summary>All registered Elasticsearch type contexts.</summary>
	IReadOnlyList<ElasticsearchTypeContext> All { get; }

	/// <summary>Gets field metadata for a mapped type, or null if not registered.</summary>
	TypeFieldMetadata? GetTypeMetadata(Type type);
}
