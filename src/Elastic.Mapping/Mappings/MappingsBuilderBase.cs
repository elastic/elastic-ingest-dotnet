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
	private readonly List<(string Path, IFieldDefinition Definition, FieldContainer Container)> _fields = [];
	private readonly List<(string Name, RuntimeFieldDefinition Definition)> _runtimeFields = [];
	private readonly List<DynamicTemplateDefinition> _dynamicTemplates = [];

	private TSelf AddFieldCore(string path, Func<FieldBuilder, FieldBuilder> configure, FieldContainer container)
	{
		var builder = new FieldBuilder();
		_ = configure(builder);
		_fields.Add((path, builder.GetDefinition(), container));
		return (TSelf)this;
	}

	/// <summary>
	/// Adds a field definition at the specified path.
	/// Called by generated property methods.
	/// </summary>
	protected TSelf AddPropertyField(string path, Func<FieldBuilder, FieldBuilder> configure) =>
		AddFieldCore(path, configure, FieldContainer.Auto);

	/// <summary>
	/// Adds a field definition directly at the specified path.
	/// Called by generated type-constrained property methods.
	/// </summary>
	protected TSelf AddFieldDirect(string path, IFieldDefinition definition)
	{
		_fields.Add((path, definition, FieldContainer.Auto));
		return (TSelf)this;
	}

	/// <summary>
	/// Merges nested field definitions from a nested builder.
	/// Called by generated nested property methods.
	/// </summary>
	protected void MergeNestedFields(IReadOnlyList<(string Path, IFieldDefinition Definition)> fields)
	{
		foreach (var (path, def) in fields)
			_fields.Add((path, def, FieldContainer.Auto));
	}

	/// <summary>
	/// Adds a multi-field under the parent field's <c>"fields"</c> container.
	/// For dotted paths (e.g. <c>"title.semantic_text"</c>) the leaf is always placed in the
	/// immediate parent's <c>fields</c> — the parent must be a leaf type (text, keyword, date, etc.).
	/// Throws at build if the parent is an object or nested type; use <see cref="AddProperty"/> instead.
	/// For single-segment paths the field is placed directly in the root <c>properties</c>.
	/// </summary>
	public TSelf AddField(string name, Func<FieldBuilder, FieldBuilder> configure) =>
		AddFieldCore(name, configure, FieldContainer.Field);

	/// <summary>
	/// Adds a sub-property under the parent field's <c>"properties"</c> container.
	/// For dotted paths (e.g. <c>"applies_to.type"</c>) the leaf is always placed in the
	/// immediate parent's <c>properties</c> — the parent must be an object or nested type.
	/// Throws at build if the parent is a leaf type; use <see cref="AddField"/> instead.
	/// Creates a new <c>type: object</c> parent when no parent is defined yet.
	/// For single-segment paths the field is placed directly in the root <c>properties</c>.
	/// </summary>
	public TSelf AddProperty(string name, Func<FieldBuilder, FieldBuilder> configure) =>
		AddFieldCore(name, configure, FieldContainer.Property);

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
	/// Builds the mapping overrides from all configured fields, runtime fields, and dynamic templates.
	/// Later entries override earlier ones when paths collide.
	/// </summary>
	public MappingOverrides Build()
	{
		var fields = new Dictionary<string, IFieldDefinition>();
		var containers = new Dictionary<string, FieldContainer>();
		foreach (var (path, def, container) in _fields)
		{
			fields[path] = def;
			containers[path] = container;
		}

		var runtimeFields = new Dictionary<string, RuntimeFieldDefinition>();
		foreach (var (name, def) in _runtimeFields)
			runtimeFields[name] = def;

		return new(fields, runtimeFields, [.. _dynamicTemplates], containers);
	}
}
