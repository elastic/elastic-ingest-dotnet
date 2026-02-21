// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Mappings;

namespace Elastic.Mapping.Tests;

public class MappingsBuilderTests
{
	[Test]
	public void MappingsBuilder_PropertyMethod_ConfiguresField()
	{
		var builder = new LogEntryMappingsBuilder();

		builder.Message(f => f.Analyzer("custom_analyzer"));

		builder.HasConfiguration.Should().BeTrue();
	}

	[Test]
	public void MappingsBuilder_AddField_AppearsInMergedJson()
	{
		var builder = new LogEntryMappingsBuilder();
		builder.AddField("all_text", f => f.Text().Analyzer("standard"));

		var overrides = GetOverrides(builder);
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		merged.Should().Contain("all_text");
	}

	[Test]
	public void MappingsBuilder_AddRuntimeField_AppearsInMergedJson()
	{
		var builder = new LogEntryMappingsBuilder();
		builder.AddRuntimeField("day_of_week", r => r
			.Keyword()
			.Script("emit(doc['@timestamp'].value.dayOfWeekEnum.getDisplayName(TextStyle.FULL, Locale.ROOT))")
		);

		var overrides = GetOverrides(builder);
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		merged.Should().Contain("runtime");
		merged.Should().Contain("day_of_week");
		merged.Should().Contain("keyword");
	}

	[Test]
	public void MappingsBuilder_AddDynamicTemplate_AppearsInMergedJson()
	{
		var builder = new LogEntryMappingsBuilder();
		builder.AddDynamicTemplate("strings_as_keywords", t => t
			.MatchMappingType("string")
			.Mapping(f => f.Keyword())
		);

		var overrides = GetOverrides(builder);
		var baseMappings = TestMappingContext.LogEntry.GetMappingJson();
		var merged = overrides.MergeIntoMappings(baseMappings);

		merged.Should().Contain("dynamic_templates");
		merged.Should().Contain("strings_as_keywords");
	}

	[Test]
	public void MappingsBuilder_HasConfiguration_FalseWhenEmpty()
	{
		var builder = new LogEntryMappingsBuilder();

		builder.HasConfiguration.Should().BeFalse();
	}

	[Test]
	public void MappingsBuilder_ChainsMultipleOperations()
	{
		var builder = new LogEntryMappingsBuilder()
			.Message(f => f.Analyzer("custom"))
			.AddField("extra", f => f.Keyword())
			.AddRuntimeField("computed", r => r.Long().Script("emit(1)"));

		builder.HasConfiguration.Should().BeTrue();
	}

	/// <summary>Uses reflection to access the internal Build method for testing.</summary>
	private static MappingOverrides GetOverrides<T>(MappingsBuilderBase<T> builder) where T : MappingsBuilderBase<T>
	{
		var method = typeof(MappingsBuilderBase<T>).GetMethod(
			"Build",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
		);
		return (MappingOverrides)method!.Invoke(builder, null)!;
	}
}
