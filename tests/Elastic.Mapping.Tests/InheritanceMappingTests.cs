// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping.Tests;

/// <summary>
/// Verifies that the source generator correctly handles type inheritance:
/// - Base-type properties appear in mapping JSON, Fields accessors, and hashes.
/// - Generic-constrained extension methods compile and are callable from helpers
///   constrained to a base type.
/// - Intermediate types that are both directly registered and a base of another
///   registered type do not produce CS0101 duplicate-class errors (partial classes).
/// - 3-level chains (DerivedPage : IntermediatePage : InheritanceBase) produce
///   the full flattened mapping.
/// </summary>
public class InheritanceMappingTests
{
	// ── JSON: base fields present in derived-type mappings ───────────────────

	[Test]
	public void IntermediatePage_MappingJson_ContainsInheritedBaseFields()
	{
		var json = InheritanceMappingContext.IntermediatePage.GetMappingJson();
		// Own field
		json.Should().Contain("\"section\"");
		// InheritanceBase fields
		json.Should().Contain("\"title\"");
		json.Should().Contain("\"status\"");
		json.Should().Contain("\"id\"");
	}

	[Test]
	public void DerivedPage_MappingJson_ContainsAllThreeLevels()
	{
		var json = InheritanceMappingContext.DerivedPage.GetMappingJson();
		// Own field
		json.Should().Contain("\"content\"");
		// IntermediatePage field
		json.Should().Contain("\"section\"");
		// InheritanceBase fields
		json.Should().Contain("\"title\"");
		json.Should().Contain("\"status\"");
		json.Should().Contain("\"id\"");
	}

	[Test]
	public void DerivedPage_MappingJson_TitleHasStandardAnalyzer()
	{
		// DerivedPageConfig.AddBaseOverrides calls .Title(f => f.Analyzer("standard"))
		// via the generic-constrained extension; verify the override was applied.
		var json = InheritanceMappingContext.DerivedPage.GetMappingJson();
		json.Should().Contain("\"analyzer\":\"standard\"");
	}

	[Test]
	public void DerivedPage_MappingJson_SectionHasIgnoreAbove()
	{
		// DerivedPageConfig.AddIntermediateOverrides calls .Section(f => f.IgnoreAbove(256))
		var json = InheritanceMappingContext.DerivedPage.GetMappingJson();
		json.Should().Contain("\"ignore_above\":256");
	}

	// ── Fields accessors: base properties reachable by name ──────────────────

	[Test]
	public void IntermediatePage_Fields_ExposesInheritedAndOwnFieldNames()
	{
		InheritanceMappingContext.IntermediatePage.Fields.Title.Should().Be("title");
		InheritanceMappingContext.IntermediatePage.Fields.Status.Should().Be("status");
		InheritanceMappingContext.IntermediatePage.Fields.Id.Should().Be("id");
		InheritanceMappingContext.IntermediatePage.Fields.Section.Should().Be("section");
	}

	[Test]
	public void DerivedPage_Fields_ExposesAllThreeLevels()
	{
		InheritanceMappingContext.DerivedPage.Fields.Content.Should().Be("content");
		InheritanceMappingContext.DerivedPage.Fields.Section.Should().Be("section");
		InheritanceMappingContext.DerivedPage.Fields.Title.Should().Be("title");
		InheritanceMappingContext.DerivedPage.Fields.Status.Should().Be("status");
		InheritanceMappingContext.DerivedPage.Fields.Id.Should().Be("id");
	}

	// ── FieldMapping dictionaries ─────────────────────────────────────────────

	[Test]
	public void IntermediatePage_FieldMapping_ContainsInheritedProps()
	{
		var map = InheritanceMappingContext.IntermediatePage.FieldMapping.PropertyToField;
		map.Should().ContainKey("Title").WhoseValue.Should().Be("title");
		map.Should().ContainKey("Status").WhoseValue.Should().Be("status");
		map.Should().ContainKey("Section").WhoseValue.Should().Be("section");
	}

	[Test]
	public void DerivedPage_FieldMapping_ContainsAllThreeLevels()
	{
		var map = InheritanceMappingContext.DerivedPage.FieldMapping.PropertyToField;
		map.Should().ContainKey("Content").WhoseValue.Should().Be("content");
		map.Should().ContainKey("Section").WhoseValue.Should().Be("section");
		map.Should().ContainKey("Title").WhoseValue.Should().Be("title");
		map.Should().ContainKey("Status").WhoseValue.Should().Be("status");
	}

	// ── Hash stability: base fields must be included in the hash ─────────────

	[Test]
	public void IntermediatePage_Hash_IsStableAndNonEmpty()
	{
		var h1 = InheritanceMappingContext.IntermediatePage.Hash;
		var h2 = InheritanceMappingContext.IntermediatePage.Hash;
		h1.Should().NotBeNullOrEmpty();
		h1.Should().Be(h2);
	}

	[Test]
	public void DerivedPage_Hash_DiffersFromIntermediatePage()
	{
		// DerivedPage has extra own fields + different analysis — must produce a different hash.
		var intermediate = InheritanceMappingContext.IntermediatePage.Hash;
		var derived = InheritanceMappingContext.DerivedPage.Hash;
		derived.Should().NotBe(intermediate);
	}

	// ── Generic-constrained extension methods compile and run ─────────────────

	[Test]
	public void BaseConstrainedHelper_CanCallGeneratedMethods_WithConcreteBuilder()
	{
		// Simulates the SharedMappingConfig pattern: a generic helper constrained to
		// InheritanceBase that calls .Title(...) — proves the generic extension method
		// is emitted and callable from a concrete MappingsBuilder<IntermediatePage>.
		var builder = new Elastic.Mapping.Mappings.MappingsBuilder<IntermediatePage>();

		var overrides = ConfigureViaBaseHelper(builder).Build();
		var json = overrides.MergeIntoMappings("{}");

		json.Should().Contain("\"analyzer\":\"keyword\"");
	}

	[Test]
	public void IntermediateConstrainedHelper_CanCallGeneratedMethods_WithDerivedBuilder()
	{
		// Proves Section<TDoc> where TDoc:IntermediatePage is callable from
		// a MappingsBuilder<DerivedPage>.
		var builder = new Elastic.Mapping.Mappings.MappingsBuilder<DerivedPage>();

		var overrides = ConfigureViaSectionHelper(builder).Build();
		var json = overrides.MergeIntoMappings("{}");

		json.Should().Contain("\"ignore_above\":512");
	}

	// These private helpers are the compile-time proof: they won't compile unless
	// the generator emitted Title<TDoc>/Section<TDoc> with the correct constraints.
	private static Elastic.Mapping.Mappings.MappingsBuilder<T> ConfigureViaBaseHelper<T>(
		Elastic.Mapping.Mappings.MappingsBuilder<T> m) where T : InheritanceBase =>
		m.Title(f => f.Analyzer("keyword"));

	private static Elastic.Mapping.Mappings.MappingsBuilder<T> ConfigureViaSectionHelper<T>(
		Elastic.Mapping.Mappings.MappingsBuilder<T> m) where T : IntermediatePage =>
		m.Section(f => f.IgnoreAbove(512));

	// ── [Id] on a base class: accessor delegate + keyword type ───────────────

	[Test]
	public void IntermediatePage_GetId_ReturnsInheritedIdValue()
	{
		// [Id] is declared on InheritanceBase; IntermediatePage inherits it.
		// The generated GetId delegate must cast to IntermediatePage and access Id.
		var ctx = InheritanceMappingContext.IntermediatePage.Context;
		var doc = new IntermediatePage { Id = "abc-123" };
		ctx.GetId!(doc).Should().Be("abc-123");
	}

	[Test]
	public void DerivedPage_GetId_ReturnsIdFromThreeLevelChain()
	{
		// [Id] flows through InheritanceBase → IntermediatePage → DerivedPage.
		var ctx = InheritanceMappingContext.DerivedPage.Context;
		var doc = new DerivedPage { Id = "xyz-789" };
		ctx.GetId!(doc).Should().Be("xyz-789");
	}

	[Test]
	public void InheritedId_IsMappedAsKeyword()
	{
		// [Id][Keyword] on InheritanceBase must produce a keyword-typed field
		// in the mapping JSON for every derived type.
		//
		// IntermediatePage has no ConfigureMappings, so GetMappingJson() returns
		// the raw pretty-printed generator output.  DerivedPage has DerivedPageConfig,
		// so its JSON is produced by MergeIntoMappings (compact, no spaces).
		var intermediateJson = InheritanceMappingContext.IntermediatePage.GetMappingJson();
		var derivedJson      = InheritanceMappingContext.DerivedPage.GetMappingJson();

		intermediateJson.Should().Contain("\"id\": { \"type\": \"keyword\"");  // pretty
		derivedJson.Should().Contain("\"id\":{\"type\":\"keyword\"");           // compact
	}
}
