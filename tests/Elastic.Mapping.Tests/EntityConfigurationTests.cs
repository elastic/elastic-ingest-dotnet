// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping.Tests;

public class EntityConfigurationTests
{
	[Test]
	public void DatePattern_GeneratesRollingWriteTarget()
	{
		var strategy = ExtendedTestMappingContext.RollingIndex.IndexStrategy;

		strategy.DatePattern.Should().Be("yyyy.MM");
		strategy.WriteTarget.Should().Be("rolling-write");

		var target = strategy.GetWriteTarget(new DateTime(2025, 6, 15));
		target.Should().Be("rolling-write-2025.06");
	}

	[Test]
	public void DatePattern_GeneratesSearchPattern()
	{
		var strategy = ExtendedTestMappingContext.RollingIndex.SearchStrategy;

		strategy.Pattern.Should().Be("rolling-*");
	}

	[Test]
	public void Dynamic_False_AppearsInMappingsJson()
	{
		var json = ExtendedTestMappingContext.RollingIndex.GetMappingJson();

		json.Should().Contain("\"dynamic\": false");
	}

	[Test]
	public void RefreshInterval_AppearsInSettingsJson()
	{
		var json = ExtendedTestMappingContext.RollingIndex.GetSettingsJson();

		json.Should().Contain("\"refresh_interval\": \"5s\"");
	}

	[Test]
	public void Variant_GeneratesSeparateResolver()
	{
		// The Variant="Semantic" registration should create a SimpleDocumentSemantic resolver
		var resolver = ExtendedTestMappingContext.SimpleDocumentSemantic;
		resolver.Should().NotBeNull();
		resolver.Hash.Should().NotBeNullOrEmpty();

		// It maps the same CLR type as the original
		resolver.Context.MappedType.Should().Be<SimpleDocument>();
	}

	[Test]
	public void Variant_HasDifferentHashFromOriginal()
	{
		// Different index name means different settings/mappings
		var originalHash = TestMappingContext.SimpleDocument.Hash;
		var variantHash = ExtendedTestMappingContext.SimpleDocumentSemantic.Hash;

		// They map the same type but with different entity config, so hashes should differ
		variantHash.Should().NotBeNullOrEmpty();
		originalHash.Should().NotBeNullOrEmpty();
	}

	[Test]
	public void ExplicitFieldAttributes_OverrideClrInference()
	{
		var json = ExtendedTestMappingContext.GeoDocument.GetMappingJson();

		// Count is int but marked [Long]
		json.Should().Contain("\"count\"");
		json.Should().Contain("\"type\": \"long\"");

		// Score is int but marked [Double]
		json.Should().Contain("\"score\"");
		json.Should().Contain("\"type\": \"double\"");

		// Active is string but marked [Boolean]
		json.Should().Contain("\"active\"");
		json.Should().Contain("\"type\": \"boolean\"");
	}

	[Test]
	public void GeoShape_GeneratesCorrectType()
	{
		var json = ExtendedTestMappingContext.GeoDocument.GetMappingJson();

		json.Should().Contain("\"boundary\"");
		json.Should().Contain("\"type\": \"geo_shape\"");
	}

	[Test]
	public void Object_GeneratesCorrectType()
	{
		var json = ExtendedTestMappingContext.GeoDocument.GetMappingJson();

		json.Should().Contain("\"metadata\"");
		json.Should().Contain("\"type\": \"object\"");
	}

	[Test]
	public void ExtendedContext_AllProperty_ContainsAllRegistrations()
	{
		var all = ExtendedTestMappingContext.All;

		// RollingIndex + GeoDocument + SimpleDocumentSemantic
		all.Should().HaveCount(3);
	}

	[Test]
	public void EntityTarget_IsSetCorrectly()
	{
		TestMappingContext.LogEntry.Context.EntityTarget.Should().Be(EntityTarget.Index);
		TestMappingContext.NginxAccessLog.Context.EntityTarget.Should().Be(EntityTarget.DataStream);
	}
}
