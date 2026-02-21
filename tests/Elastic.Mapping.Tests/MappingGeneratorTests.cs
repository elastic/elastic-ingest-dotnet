// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping.Tests;

public class MappingGeneratorTests
{
	[Test]
	public void Index_GeneratesStaticMappingClass()
	{
		var hash = TestMappingContext.LogEntry.Hash;
		hash.Should().NotBeNullOrEmpty();
		hash.Should().HaveLength(16);
	}

	[Test]
	public void Index_GeneratesFieldConstants()
	{
		TestMappingContext.LogEntry.Fields.Timestamp.Should().Be("@timestamp");
		TestMappingContext.LogEntry.Fields.Level.Should().Be("log.level");
		TestMappingContext.LogEntry.Fields.Message.Should().Be("message");
		TestMappingContext.LogEntry.Fields.StatusCode.Should().Be("statusCode");
	}

	[Test]
	public void Index_GeneratesIndexStrategy()
	{
		var strategy = TestMappingContext.LogEntry.IndexStrategy;
		strategy.WriteTarget.Should().Be("logs-write");
	}

	[Test]
	public void Index_GeneratesSearchStrategy()
	{
		var strategy = TestMappingContext.LogEntry.SearchStrategy;
		strategy.Pattern.Should().Be("logs-*");
		strategy.ReadAlias.Should().Be("logs-read");
	}

	[Test]
	public void Index_GeneratesSettingsJson()
	{
		var json = TestMappingContext.LogEntry.GetSettingsJson();
		json.Should().Contain("\"number_of_shards\": 3");
		json.Should().Contain("\"number_of_replicas\": 2");
	}

	[Test]
	public void Index_GeneratesMappingJson()
	{
		var json = TestMappingContext.LogEntry.GetMappingJson();
		json.Should().Contain("\"@timestamp\"");
		json.Should().Contain("\"type\": \"date\"");
		json.Should().Contain("\"type\": \"keyword\"");
		json.Should().Contain("\"type\": \"text\"");
		json.Should().Contain("\"type\": \"ip\"");
		// InternalId should not be in mapping (JsonIgnore)
		json.Should().NotContain("internalId");
	}

	[Test]
	public void Index_GeneratesIndexJson()
	{
		var json = TestMappingContext.LogEntry.GetIndexJson();
		json.Should().Contain("\"settings\":");
		json.Should().Contain("\"mappings\":");
	}

	[Test]
	public void Index_HashIsStable()
	{
		var hash1 = TestMappingContext.LogEntry.Hash;
		var hash2 = TestMappingContext.LogEntry.Hash;
		hash1.Should().Be(hash2);
	}

	[Test]
	public void Index_SeparateHashesProvided()
	{
		var settingsHash = TestMappingContext.LogEntry.SettingsHash;
		var mappingsHash = TestMappingContext.LogEntry.MappingsHash;

		settingsHash.Should().NotBeNullOrEmpty();
		mappingsHash.Should().NotBeNullOrEmpty();
		settingsHash.Should().NotBe(mappingsHash);
	}

	[Test]
	public void DataStream_GeneratesCorrectStrategy()
	{
		var indexStrategy = TestMappingContext.NginxAccessLog.IndexStrategy;
		indexStrategy.DataStreamName.Should().Be("logs-nginx.access-production");
		indexStrategy.Type.Should().Be("logs");
		indexStrategy.Dataset.Should().Be("nginx.access");
		indexStrategy.Namespace.Should().Be("production");

		var searchStrategy = TestMappingContext.NginxAccessLog.SearchStrategy;
		searchStrategy.Pattern.Should().Be("logs-nginx.access-*");
	}

	[Test]
	public void SimpleDocument_InfersTypesFromClrTypes()
	{
		var json = TestMappingContext.SimpleDocument.GetMappingJson();
		json.Should().Contain("\"name\": { \"type\": \"text\"");
		json.Should().Contain("\"value\": { \"type\": \"integer\"");
		json.Should().Contain("\"createdAt\": { \"type\": \"date\"");
	}

	[Test]
	public void AdvancedDocument_SupportsSpecializedTypes()
	{
		var json = TestMappingContext.AdvancedDocument.GetMappingJson();
		json.Should().Contain("\"type\": \"geo_point\"");
		json.Should().Contain("\"type\": \"dense_vector\"");
		json.Should().Contain("\"dims\": 384");
		json.Should().Contain("\"similarity\": \"cosine\"");
		json.Should().Contain("\"type\": \"semantic_text\"");
		json.Should().Contain("\"type\": \"completion\"");
		json.Should().Contain("\"type\": \"nested\"");
	}

	[Test]
	public void Index_GeneratesFieldMappingDictionaries()
	{
		var propertyToField = TestMappingContext.LogEntry.FieldMapping.PropertyToField;
		propertyToField["Timestamp"].Should().Be("@timestamp");
		propertyToField["Level"].Should().Be("log.level");
		propertyToField["Message"].Should().Be("message");
		propertyToField["StatusCode"].Should().Be("statusCode");

		var fieldToProperty = TestMappingContext.LogEntry.FieldMapping.FieldToProperty;
		fieldToProperty["@timestamp"].Should().Be("Timestamp");
		fieldToProperty["log.level"].Should().Be("Level");
		fieldToProperty["message"].Should().Be("Message");
		fieldToProperty["statusCode"].Should().Be("StatusCode");
	}

	[Test]
	public void Index_GeneratesIgnoredPropertiesSet()
	{
		var ignoredProperties = TestMappingContext.LogEntry.IgnoredProperties;
		ignoredProperties.Should().Contain("InternalId");
		ignoredProperties.Should().NotContain("Timestamp");
		ignoredProperties.Should().NotContain("Message");
	}

	[Test]
	public void Index_GeneratesGetPropertyMap()
	{
		var propertyMap = TestMappingContext.LogEntry.GetPropertyMap();

		propertyMap["@timestamp"].Name.Should().Be("Timestamp");
		propertyMap["log.level"].Name.Should().Be("Level");
		propertyMap["message"].Name.Should().Be("Message");

		propertyMap["Timestamp"].Name.Should().Be("Timestamp");
		propertyMap["Level"].Name.Should().Be("Level");

		propertyMap.Should().NotContainKey("InternalId");
		propertyMap.Should().NotContainKey("internalId");
	}

	[Test]
	public void SimpleDocument_GeneratesEmptyIgnoredPropertiesSet()
	{
		var ignoredProperties = TestMappingContext.SimpleDocument.IgnoredProperties;
		ignoredProperties.Should().BeEmpty();
	}

	[Test]
	public void Context_AllProperty_ContainsAllRegistrations()
	{
		var all = TestMappingContext.All;
		all.Should().HaveCount(4);
	}

	[Test]
	public void Context_AllProperty_ContainsValidContexts()
	{
		foreach (var ctx in TestMappingContext.All)
		{
			ctx.Hash.Should().NotBeNullOrEmpty();
			ctx.GetSettingsJson().Should().NotBeNullOrEmpty();
			ctx.GetMappingsJson().Should().NotBeNullOrEmpty();
		}
	}

	[Test]
	public void Context_ConfigureAnalysis_DelegateIsWired()
	{
		// LogEntry ConfigureAnalysis is on the context, delegate should still be set
		TestMappingContext.LogEntry.Context.ConfigureAnalysis.Should().NotBeNull();
	}

	[Test]
	public void Context_ConfigureAnalysis_ProducesAnalysisComponents()
	{
		// Verify analysis components are generated from context method
		var builder = new Analysis.AnalysisBuilder();
		var result = TestMappingContext.LogEntry.Context.ConfigureAnalysis!(builder);
		result.Should().NotBeNull();
	}

	[Test]
	public void Context_MixedStrategies_AllCompile()
	{
		TestMappingContext.All.Should().HaveCount(4);
		foreach (var ctx in TestMappingContext.All)
			ctx.Hash.Should().NotBeNullOrEmpty();
	}
}
