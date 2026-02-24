// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Provides access to type field metadata from a generated mapping context.
/// </summary>
public interface IElasticsearchMappingContext
{
	/// <summary>Type field metadata for all registered document types, keyed by CLR type.</summary>
	IReadOnlyDictionary<Type, TypeFieldMetadata> All { get; }
}
