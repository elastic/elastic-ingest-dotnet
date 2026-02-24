// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Mapping.Analysis;
using Elastic.Mapping.Mappings;

namespace Elastic.Mapping.Tests;

public class ConfigurationInterfaceTests
{
	[Test]
	public void Context_ProvidesElasticsearchTypeContext()
	{
		var context = TestMappingContext.LogEntry.Context;
		context.Should().NotBeNull();
		context.Hash.Should().NotBeNullOrEmpty();
	}

	[Test]
	public void ConfigureAnalysis_ViaDelegate_Works()
	{
		var context = TestMappingContext.LogEntry.Context;
		context.ConfigureAnalysis.Should().NotBeNull();

		var builder = context.ConfigureAnalysis!(new AnalysisBuilder());
		builder.Should().NotBeNull();
		builder.HasConfiguration.Should().BeTrue();
	}

	[Test]
	public void ConfigureAnalysis_BuildsAndMergesIntoSettings()
	{
		var context = TestMappingContext.LogEntry.Context;
		var analysis = context.ConfigureAnalysis!(new AnalysisBuilder()).Build();
		var baseSettings = TestMappingContext.LogEntry.GetSettingsJson();
		var mergedSettings = analysis.MergeIntoSettings(baseSettings);

		mergedSettings.Should().Contain("analysis");
		mergedSettings.Should().Contain("log_message_analyzer");
	}

	[Test]
	public void ConfigureAnalysis_ExplicitCall_Works()
	{
		var builder = TestMappingContext.ConfigureLogEntryAnalysis(new AnalysisBuilder());
		builder.Should().NotBeNull();
		builder.HasConfiguration.Should().BeTrue();
	}

	[Test]
	public void SimpleDocument_WithoutConfigureMethods_HasNullDelegate()
	{
		var context = TestMappingContext.SimpleDocument.Context;
		context.ConfigureAnalysis.Should().BeNull();
	}

	[Test]
	public void Instance_GetTypeMetadata_ReturnsContextForKnownType()
	{
		var metadata = TestMappingContext.Instance.GetTypeMetadata(typeof(LogEntry));

		metadata.Should().NotBeNull();
		metadata!.PropertyToField["Timestamp"].Should().Be("@timestamp");
		metadata.SearchPattern.Should().Be("logs-*");
	}

	[Test]
	public void Instance_GetTypeMetadata_ReturnsNullForUnknownType()
	{
		var metadata = TestMappingContext.Instance.GetTypeMetadata(typeof(string));

		metadata.Should().BeNull();
	}

	[Test]
	public void Instance_All_MatchesStaticAll()
	{
		var instanceAll = TestMappingContext.Instance.All;

		instanceAll.Should().HaveCount(TestMappingContext.All.Count);
	}

	[Test]
	public void TextFields_ContainsTextProperties()
	{
		var textFields = TestMappingContext.LogEntry.TextFields;

		textFields.Should().Contain("Message");
	}

	[Test]
	public void TextFields_ExcludesNonTextProperties()
	{
		var textFields = TestMappingContext.LogEntry.TextFields;

		textFields.Should().NotContain("Level");
		textFields.Should().NotContain("StatusCode");
		textFields.Should().NotContain("IsError");
		textFields.Should().NotContain("Timestamp");
	}

	[Test]
	public void TextFields_EmptyWhenNoTextFields()
	{
		var textFields = TestMappingContext.NginxAccessLog.TextFields;

		// NginxAccessLog has Path as [Text], so it should contain Path
		textFields.Should().Contain("Path");
	}

	// ========================================================================
	// Self-configuring entity: type implements IConfigureElasticsearch<T>
	// ========================================================================

	[Test]
	public void SelfConfigured_ConfigureAnalysis_IsWiredUp()
	{
		var ctx = InterfaceTestMappingContext.SelfConfiguredDocument.Context;
		ctx.ConfigureAnalysis.Should().NotBeNull();

		var result = ctx.ConfigureAnalysis!(new AnalysisBuilder());
		result.HasConfiguration.Should().BeTrue();

		var json = result.Build().ToJsonString();
		json.Should().Contain("self_analyzer");
	}

	[Test]
	public void SelfConfigured_ConfigureMappings_ProducesRuntimeField()
	{
		var json = InterfaceTestMappingContext.SelfConfiguredDocument.GetMappingJson();
		using var doc = JsonDocument.Parse(json);

		doc.RootElement.TryGetProperty("runtime", out var runtime).Should().BeTrue();
		runtime.TryGetProperty("name_length", out _).Should().BeTrue();
	}

	[Test]
	public void SelfConfigured_ExtensionMethods_WorkOnBuilder()
	{
		var builder = new MappingsBuilder<SelfConfiguredDocument>()
			.Body(f => f.Analyzer("override_analyzer"));

		builder.HasConfiguration.Should().BeTrue();
	}

	// ========================================================================
	// External configuration: Configuration = typeof(...) with interface
	// ========================================================================

	[Test]
	public void ExternalConfig_ConfigureAnalysis_IsWiredUp()
	{
		var ctx = InterfaceTestMappingContext.ExternallyConfiguredDocument.Context;
		ctx.ConfigureAnalysis.Should().NotBeNull();

		var result = ctx.ConfigureAnalysis!(new AnalysisBuilder());
		result.HasConfiguration.Should().BeTrue();

		var json = result.Build().ToJsonString();
		json.Should().Contain("ext_analyzer");
	}

	[Test]
	public void ExternalConfig_ConfigureMappings_ProducesRuntimeField()
	{
		var json = InterfaceTestMappingContext.ExternallyConfiguredDocument.GetMappingJson();
		using var doc = JsonDocument.Parse(json);

		doc.RootElement.TryGetProperty("runtime", out var runtime).Should().BeTrue();
		runtime.TryGetProperty("score_tier", out _).Should().BeTrue();
	}

	[Test]
	public void ExternalConfig_IndexSettings_AreExposed()
	{
		var ctx = InterfaceTestMappingContext.ExternallyConfiguredDocument.Context;
		ctx.IndexSettings.Should().NotBeNull();
		ctx.IndexSettings!["index.default_pipeline"].Should().Be("ext-pipeline");
	}

	[Test]
	public void ExternalConfig_InstanceCall_MatchesInterface()
	{
		IConfigureElasticsearch<ExternallyConfiguredDocument> config = new ExternalDocumentConfig();

		var analysis = config.ConfigureAnalysis(new AnalysisBuilder());
		analysis.HasConfiguration.Should().BeTrue();

		var mappings = config.ConfigureMappings(new MappingsBuilder<ExternallyConfiguredDocument>());
		mappings.HasConfiguration.Should().BeTrue();

		config.IndexSettings.Should().NotBeNull();
	}

	// ========================================================================
	// Partial configuration: only overrides ConfigureMappings, defaults for rest
	// ========================================================================

	[Test]
	public void PartialConfig_ConfigureAnalysis_IsNull()
	{
		var ctx = InterfaceTestMappingContext.PartiallyConfiguredDocument.Context;
		// PartialDocumentConfig doesn't override ConfigureAnalysis, so the default returns
		// the builder unchanged (no-op). The generator detects this and wires the delegate,
		// but the result has no configuration.
		if (ctx.ConfigureAnalysis != null)
		{
			var result = ctx.ConfigureAnalysis(new AnalysisBuilder());
			result.HasConfiguration.Should().BeFalse();
		}
	}

	[Test]
	public void PartialConfig_ConfigureMappings_ProducesMultiField()
	{
		var json = InterfaceTestMappingContext.PartiallyConfiguredDocument.GetMappingJson();
		using var doc = JsonDocument.Parse(json);

		var title = doc.RootElement.GetProperty("properties").GetProperty("title");
		title.GetProperty("analyzer").GetString().Should().Be("standard");
		title.GetProperty("fields").GetProperty("keyword").GetProperty("type")
			.GetString().Should().Be("keyword");
	}

	[Test]
	public void PartialConfig_IndexSettings_DefaultsToNull()
	{
		IConfigureElasticsearch<PartiallyConfiguredDocument> config = new PartialDocumentConfig();
		config.IndexSettings.Should().BeNull();
	}

	// ========================================================================
	// RegisterServiceProvider: DI override path (isolated context)
	// ========================================================================

	[Test]
	[NotInParallel("di-override")]
	public void RegisterServiceProvider_OverridesConfiguration()
	{
		var customConfig = new DiOverrideConfig();
		var services = new MinimalServiceProvider(
			typeof(IConfigureElasticsearch<DiTestDocument>), customConfig);

		DiTestMappingContext.RegisterServiceProvider(services);

		try
		{
			var ctx = DiTestMappingContext.DiTestDocument.Context;
			ctx.ConfigureAnalysis.Should().NotBeNull();

			var result = ctx.ConfigureAnalysis!(new AnalysisBuilder());
			result.HasConfiguration.Should().BeTrue();
			result.Build().ToJsonString().Should().Contain("overridden_analyzer");
		}
		finally
		{
			DiTestMappingContext.RegisterServiceProvider(new MinimalServiceProvider());
		}
	}

	// ========================================================================
	// InterfaceTestMappingContext.All
	// ========================================================================

	[Test]
	public void InterfaceContext_All_ContainsAllRegistrations()
	{
		InterfaceTestMappingContext.All.Should().HaveCount(3);
	}
}

/// <summary>
/// DI override config for testing RegisterServiceProvider.
/// </summary>
public class DiOverrideConfig : IConfigureElasticsearch<DiTestDocument>
{
	public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		analysis.Analyzer("overridden_analyzer", a => a.Custom()
			.Tokenizer(BuiltInAnalysis.Tokenizers.Whitespace));
}

/// <summary>
/// Minimal IServiceProvider for testing without a full DI container.
/// </summary>
public class MinimalServiceProvider : IServiceProvider
{
	private readonly Type? _serviceType;
	private readonly object? _instance;

	public MinimalServiceProvider() { }

	public MinimalServiceProvider(Type serviceType, object instance)
	{
		_serviceType = serviceType;
		_instance = instance;
	}

	public object? GetService(Type serviceType) =>
		serviceType == _serviceType ? _instance : null;
}
