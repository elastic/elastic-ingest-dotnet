// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Mappings;
using Elastic.Mapping.Tests.Contracts;

namespace Elastic.Mapping.Tests.CrossAssembly;

// ============================================================================
// CROSS-ASSEMBLY TYPES
//
// The nested-builder visibility bug triggers when:
// 1. EmitForContext processes a type with an OWN [Object] property of the same
//    nested type, emitting SharedProductNestedBuilder into THIS namespace.
// 2. EmitBaseExtensionsClass then processes inherited properties referencing the
//    same nested type, finds the name already claimed in emittedNestedBuilders,
//    and skips emitting the class — but its extension method still references
//    SharedProductNestedBuilder by unqualified name.
// 3. In the base-extensions file (emitted into the contracts assembly's namespace),
//    the unqualified name resolves to the contracts assembly's already-compiled
//    copy whose ctor/GetFields were internal.
//
// Before the fix: CS1729 (no accessible constructor) + CS1061 (GetFields not found).
// After the fix: public ctor/GetFields → cross-assembly binding works.
// ============================================================================

/// <summary>
/// Type with its OWN [Object] SharedProduct property (not inherited).
/// Registered first in the context so EmitForContext emits the nested builder
/// into the consumer's namespace, winning the dedup race.
/// </summary>
public class IndependentConsumerDocument
{
	[Id]
	[Keyword]
	public string Id { get; set; } = string.Empty;

	[Object]
	public SharedProduct? Product { get; set; }
}

/// <summary>
/// Inherits [Object] SharedProduct from <see cref="ContractDocumentBase"/>.
/// When EmitBaseExtensionsClass runs for ContractDocumentBase, it finds
/// SharedProductNestedBuilder already claimed and skips class emission.
/// Its generated extension method references SharedProductNestedBuilder
/// unqualified in the Elastic.Mapping.Tests.Contracts namespace, which
/// resolves to the contracts assembly's copy.
/// </summary>
public class ConsumerDocument : ContractDocumentBase
{
	[Text]
	public string Summary { get; set; } = string.Empty;
}

/// <summary>
/// Mapping context in the consumer assembly. Registers IndependentConsumerDocument
/// FIRST (so its EmitForContext pass claims SharedProductNestedBuilder in this namespace)
/// and ConsumerDocument SECOND (so its EmitBaseExtensionsClass pass encounters the
/// dedup collision and references the cross-assembly type).
/// </summary>
[ElasticsearchMappingContext]
[Index<IndependentConsumerDocument>(Name = "independent-consumer")]
[Index<ConsumerDocument>(Name = "consumer-docs")]
public static partial class ConsumerMappingContext;

// ============================================================================
// TESTS
// ============================================================================

/// <summary>
/// Regression tests for the cross-assembly nested-builder visibility bug.
///
/// The primary assertion is that this project COMPILES — before the fix,
/// the generated ContractDocumentBaseMappingsExtensions.g.cs would reference
/// SharedProductNestedBuilder from the contracts assembly (internal ctor and
/// GetFields), producing CS1729 and CS1061.
/// </summary>
public class CrossAssemblyNestedBuilderTests
{
	[Test]
	public void IndependentConsumer_Compiles_And_ProducesMappingJson()
	{
		var json = ConsumerMappingContext.IndependentConsumerDocument.GetMappingJson();
		json.Should().NotBeNullOrEmpty();

		using var doc = System.Text.Json.JsonDocument.Parse(json);
		var props = doc.RootElement.GetProperty("properties");
		var product = props.GetProperty("product");
		product.GetProperty("type").GetString().Should().Be("object");

		var subProps = product.GetProperty("properties");
		subProps.GetProperty("id").GetProperty("type").GetString().Should().Be("keyword");
		subProps.GetProperty("name").GetProperty("type").GetString().Should().Be("text");
	}

	[Test]
	public void ConsumerDocument_Compiles_And_ProducesMappingJson()
	{
		var json = ConsumerMappingContext.ConsumerDocument.GetMappingJson();
		json.Should().NotBeNullOrEmpty();
	}

	[Test]
	public void ConsumerDocument_MappingJson_ContainsInheritedProductField()
	{
		var json = ConsumerMappingContext.ConsumerDocument.GetMappingJson();
		using var doc = System.Text.Json.JsonDocument.Parse(json);
		var props = doc.RootElement.GetProperty("properties");

		var product = props.GetProperty("product");
		product.GetProperty("type").GetString().Should().Be("object");

		var subProps = product.GetProperty("properties");
		subProps.GetProperty("id").GetProperty("type").GetString().Should().Be("keyword");
		subProps.GetProperty("name").GetProperty("type").GetString().Should().Be("text");
	}

	[Test]
	public void ConsumerDocument_MappingJson_ContainsOwnField()
	{
		var json = ConsumerMappingContext.ConsumerDocument.GetMappingJson();
		using var doc = System.Text.Json.JsonDocument.Parse(json);
		var props = doc.RootElement.GetProperty("properties");

		props.GetProperty("summary").GetProperty("type").GetString().Should().Be("text");
	}

	[Test]
	public void ContractDocument_StillWorksFromContractsAssembly()
	{
		var json = ContractMappingContext.ContractDocument.GetMappingJson();
		json.Should().NotBeNullOrEmpty();

		using var doc = System.Text.Json.JsonDocument.Parse(json);
		var props = doc.RootElement.GetProperty("properties");

		var product = props.GetProperty("product");
		product.GetProperty("type").GetString().Should().Be("object");
	}
}
