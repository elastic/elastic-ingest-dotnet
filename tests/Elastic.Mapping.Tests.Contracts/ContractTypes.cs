// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Mapping;
using Elastic.Mapping.Mappings;

namespace Elastic.Mapping.Tests.Contracts;

/// <summary>
/// Nested type shared across base and derived document types.
/// When the generator emits the NestedBuilder for this type, its constructor
/// and GetFields() must be public so cross-assembly consumers can use them.
/// </summary>
public class SharedProduct
{
	[Keyword(Normalizer = "keyword_normalizer")]
	public string Id { get; set; } = string.Empty;

	[Text]
	public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Base document declaring an [Object]-typed property of <see cref="SharedProduct"/>.
/// The generator emits a <c>SharedProductNestedBuilder</c> when processing this type.
/// </summary>
public abstract class ContractDocumentBase
{
	[Id]
	[Keyword]
	public string DocId { get; set; } = string.Empty;

	[Object]
	public SharedProduct? Product { get; set; }
}

/// <summary>
/// Derived type registered in the contracts assembly's own context.
/// Exercises the within-assembly path — this always worked.
/// </summary>
public class ContractDocument : ContractDocumentBase
{
	[Text]
	public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Mapping context in the contracts assembly. Compiles SharedProductNestedBuilder
/// into this assembly with the constructor and GetFields() visibility set by the generator.
/// </summary>
[ElasticsearchMappingContext]
[Index<ContractDocument>(Name = "contract-docs")]
public static partial class ContractMappingContext;
