// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Mappings.Builders;
using Elastic.Mapping.Mappings.Definitions;

namespace Elastic.Mapping.Mappings;

/// <summary>
/// Abstract base class for type-specific mapping builders.
/// Generated builders inherit from this and add property-specific methods.
/// Uses CRTP (Curiously Recurring Template Pattern) to return the correct derived type.
/// </summary>
/// <typeparam name="TSelf">The derived builder type.</typeparam>
public abstract class MappingsBuilderBase<TSelf> where TSelf : MappingsBuilderBase<TSelf>
{
	private readonly List<(string Path, IFieldDefinition Definition)> _fields = [];
	private readonly List<(string Name, RuntimeFieldDefinition Definition)> _runtimeFields = [];
	private readonly List<DynamicTemplateDefinition> _dynamicTemplates = [];

	/// <summary>
	/// Adds a field definition at the specified path.
	/// Called by generated property methods.
	/// </summary>
	protected TSelf AddPropertyField(string path, Func<FieldBuilder, FieldBuilder> configure)
	{
		var builder = new FieldBuilder();
		_ = configure(builder);
		_fields.Add((path, builder.GetDefinition()));
		return (TSelf)this;
	}

	/// <summary>
	/// Adds a field definition directly at the specified path.
	/// Called by generated type-constrained property methods.
	/// </summary>
	protected TSelf AddFieldDirect(string path, IFieldDefinition definition)
	{
		_fields.Add((path, definition));
		return (TSelf)this;
	}

	/// <summary>
	/// Merges nested field definitions from a nested builder.
	/// Called by generated nested property methods.
	/// </summary>
	protected void MergeNestedFields(IReadOnlyList<(string Path, IFieldDefinition Definition)> fields)
	{
		foreach (var (path, def) in fields)
			_fields.Add((path, def));
	}

	/// <summary>
	/// Adds a field that is not a property on the model (e.g., copy_to target field).
	/// </summary>
	public TSelf AddField(string name, Func<FieldBuilder, FieldBuilder> configure)
	{
		var builder = new FieldBuilder();
		_ = configure(builder);
		_fields.Add((name, builder.GetDefinition()));
		return (TSelf)this;
	}

	/// <summary>
	/// Adds a runtime field definition.
	/// </summary>
	public TSelf AddRuntimeField(string name, Func<RuntimeFieldBuilder, RuntimeFieldBuilder> configure)
	{
		var builder = new RuntimeFieldBuilder();
		_ = configure(builder);
		_runtimeFields.Add((name, builder.GetDefinition()));
		return (TSelf)this;
	}

	/// <summary>
	/// Adds a dynamic template definition.
	/// </summary>
	public TSelf AddDynamicTemplate(string name, Func<DynamicTemplateBuilder, DynamicTemplateBuilder> configure)
	{
		var builder = new DynamicTemplateBuilder(name);
		_ = configure(builder);
		_dynamicTemplates.Add(builder.GetDefinition());
		return (TSelf)this;
	}

	/// <summary>
	/// Returns true if any mapping configurations have been added.
	/// </summary>
	public bool HasConfiguration =>
		_fields.Count > 0 ||
		_runtimeFields.Count > 0 ||
		_dynamicTemplates.Count > 0;

	/// <summary>
	/// Builds the mapping overrides. Called by framework, not by model code.
	/// </summary>
	internal MappingOverrides Build() =>
		new(
			_fields.ToDictionary(x => x.Path, x => x.Definition),
			_runtimeFields.ToDictionary(x => x.Name, x => x.Definition),
			[.. _dynamicTemplates]
		);
}
