// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using Elastic.Mapping.Generator.Model;

namespace Elastic.Mapping.Generator.Emitters;

/// <summary>
/// Emits the type-specific MappingsBuilder class for strongly-typed mapping configuration.
/// </summary>
internal static class MappingsBuilderEmitter
{
	private const string FieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.FieldBuilder";
	private const string TextFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.TextFieldBuilder";
	private const string KeywordFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.KeywordFieldBuilder";
	private const string DateFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.DateFieldBuilder";
	private const string LongFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.LongFieldBuilder";
	private const string IntegerFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.IntegerFieldBuilder";
	private const string ShortFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.ShortFieldBuilder";
	private const string ByteFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.ByteFieldBuilder";
	private const string DoubleFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.DoubleFieldBuilder";
	private const string FloatFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.FloatFieldBuilder";
	private const string BooleanFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.BooleanFieldBuilder";
	private const string IpFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.IpFieldBuilder";
	private const string GeoPointFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.GeoPointFieldBuilder";
	private const string GeoShapeFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.GeoShapeFieldBuilder";
	private const string NestedFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.NestedFieldBuilder";
	private const string ObjectFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.ObjectFieldBuilder";
	private const string CompletionFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.CompletionFieldBuilder";
	private const string DenseVectorFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.DenseVectorFieldBuilder";
	private const string SemanticTextFieldBuilderFqn = "global::Elastic.Mapping.Mappings.Builders.SemanticTextFieldBuilder";

	/// <summary>
	/// Emits MappingsBuilder for a type registration within a context.
	/// </summary>
	public static string EmitForContext(ContextMappingModel context, TypeRegistration reg)
	{
		var sb = new StringBuilder();

		SharedEmitterHelpers.EmitHeader(sb);

		if (!string.IsNullOrEmpty(context.Namespace))
		{
			sb.AppendLine($"namespace {context.Namespace};");
			sb.AppendLine();
		}

		EmitMappingsBuilderClass(sb, reg.TypeModel, reg.ResolverName, reg.AnalysisComponents);

		return sb.ToString();
	}

	private static string GetBuilderTypeForFieldType(string fieldType) =>
		fieldType switch
		{
			FieldTypes.Text => TextFieldBuilderFqn,
			FieldTypes.Keyword => KeywordFieldBuilderFqn,
			FieldTypes.Date => DateFieldBuilderFqn,
			FieldTypes.Long => LongFieldBuilderFqn,
			FieldTypes.Integer => IntegerFieldBuilderFqn,
			FieldTypes.Short => ShortFieldBuilderFqn,
			FieldTypes.Byte => ByteFieldBuilderFqn,
			FieldTypes.Double => DoubleFieldBuilderFqn,
			FieldTypes.Float => FloatFieldBuilderFqn,
			FieldTypes.Boolean => BooleanFieldBuilderFqn,
			FieldTypes.Ip => IpFieldBuilderFqn,
			FieldTypes.GeoPoint => GeoPointFieldBuilderFqn,
			FieldTypes.GeoShape => GeoShapeFieldBuilderFqn,
			FieldTypes.Nested => NestedFieldBuilderFqn,
			FieldTypes.Object => ObjectFieldBuilderFqn,
			FieldTypes.Completion => CompletionFieldBuilderFqn,
			FieldTypes.DenseVector => DenseVectorFieldBuilderFqn,
			FieldTypes.SemanticText => SemanticTextFieldBuilderFqn,
			_ => FieldBuilderFqn
		};

	private static string? GetFactoryMethodForFieldType(string fieldType) =>
		fieldType switch
		{
			FieldTypes.Text => "Text",
			FieldTypes.Keyword => "Keyword",
			FieldTypes.Date => "Date",
			FieldTypes.Long => "Long",
			FieldTypes.Integer => "Integer",
			FieldTypes.Short => "Short",
			FieldTypes.Byte => "Byte",
			FieldTypes.Double => "Double",
			FieldTypes.Float => "Float",
			FieldTypes.Boolean => "Boolean",
			FieldTypes.Ip => "Ip",
			FieldTypes.GeoPoint => "GeoPoint",
			FieldTypes.GeoShape => "GeoShape",
			FieldTypes.Nested => "Nested",
			FieldTypes.Object => "Object",
			FieldTypes.Completion => "Completion",
			FieldTypes.DenseVector => "DenseVector",
			FieldTypes.SemanticText => "SemanticText",
			_ => null
		};

	private static void EmitMappingsBuilderClass(
		StringBuilder sb,
		TypeMappingModel model,
		string typeName,
		AnalysisComponentsModel analysisComponents
	)
	{
		var indent = "";
		var builderClassName = $"{typeName}MappingsBuilder";
		var analysisPath = $"{typeName}Analysis";

		// Collect nested builders that need to be generated
		var nestedBuilders = new List<(string PropertyName, string FieldName, NestedTypeModel NestedType)>();

		sb.AppendLine($"{indent}/// <summary>Type-specific mappings builder for {typeName}.</summary>");
		sb.AppendLine($"{indent}public sealed class {builderClassName} : global::Elastic.Mapping.Mappings.MappingsBuilderBase<{builderClassName}>");
		sb.AppendLine($"{indent}{{");

		// Add Analysis accessor if there are analysis components
		if (analysisComponents.HasAnyComponents)
		{
			EmitAnalysisAccessor(sb, indent + "\t", analysisPath);
			sb.AppendLine();
		}

		// Generate a method for each non-ignored property
		var props = model.Properties.Where(p => !p.IsIgnored).ToList();
		for (var i = 0; i < props.Count; i++)
		{
			var prop = props[i];
			EmitPropertyMethod(sb, indent, builderClassName, prop);

			if (prop.NestedType != null)
				nestedBuilders.Add((prop.PropertyName, prop.FieldName, prop.NestedType));

			if (i < props.Count - 1)
				sb.AppendLine();
		}

		// Generate overloaded methods for nested properties
		foreach (var (propertyName, fieldName, nestedType) in nestedBuilders)
		{
			sb.AppendLine();
			EmitNestedPropertyOverload(sb, indent, builderClassName, propertyName, fieldName, nestedType);
		}

		sb.AppendLine($"{indent}}}");
		sb.AppendLine();

		// Generate nested builder classes (deduplicate by type name)
		var emittedNestedBuilders = new HashSet<string>();
		foreach (var (propertyName, fieldName, nestedType) in nestedBuilders)
		{
			if (emittedNestedBuilders.Add(nestedType.TypeName))
				EmitNestedBuilderClass(sb, indent, builderClassName, propertyName, fieldName, nestedType);
		}
	}

	private static void EmitAnalysisAccessor(StringBuilder sb, string indent, string analysisPath)
	{
		sb.AppendLine($"{indent}/// <summary>Provides instance-based access to analysis component names.</summary>");
		sb.AppendLine($"{indent}public sealed class AnalysisAccessor");
		sb.AppendLine($"{indent}{{");
		sb.AppendLine($"{indent}\t/// <summary>Analyzer names (built-in and custom).</summary>");
		sb.AppendLine($"{indent}\tpublic {analysisPath}.AnalyzersAccessor Analyzers => {analysisPath}.Analyzers;");
		sb.AppendLine($"{indent}\t/// <summary>Tokenizer names (built-in and custom).</summary>");
		sb.AppendLine($"{indent}\tpublic {analysisPath}.TokenizersAccessor Tokenizers => {analysisPath}.Tokenizers;");
		sb.AppendLine($"{indent}\t/// <summary>Token filter names (built-in and custom).</summary>");
		sb.AppendLine($"{indent}\tpublic {analysisPath}.TokenFiltersAccessor TokenFilters => {analysisPath}.TokenFilters;");
		sb.AppendLine($"{indent}\t/// <summary>Char filter names (built-in and custom).</summary>");
		sb.AppendLine($"{indent}\tpublic {analysisPath}.CharFiltersAccessor CharFilters => {analysisPath}.CharFilters;");
		sb.AppendLine($"{indent}\t/// <summary>Normalizer names (custom).</summary>");
		sb.AppendLine($"{indent}\tpublic {analysisPath}.NormalizersAccessor Normalizers => {analysisPath}.Normalizers;");
		sb.AppendLine($"{indent}}}");
		sb.AppendLine();
		sb.AppendLine($"{indent}private static readonly AnalysisAccessor _analysis = new();");
		sb.AppendLine($"{indent}/// <summary>Analysis component names for use in mapping configuration.</summary>");
		sb.AppendLine($"{indent}public AnalysisAccessor Analysis => _analysis;");
	}

	private static void EmitNestedPropertyOverload(
		StringBuilder sb,
		string indent,
		string builderClassName,
		string propertyName,
		string fieldName,
		NestedTypeModel nestedType
	)
	{
		var nestedBuilderClassName = $"{builderClassName}_{nestedType.TypeName}NestedBuilder";

		sb.AppendLine($"{indent}\t/// <summary>Configure nested properties of the {propertyName} field (maps to \"{fieldName}\").</summary>");
		sb.AppendLine($"{indent}\tpublic {builderClassName} {propertyName}(global::System.Func<{nestedBuilderClassName}, {nestedBuilderClassName}> configure)");
		sb.AppendLine($"{indent}\t{{");
		sb.AppendLine($"{indent}\t\tvar nested = new {nestedBuilderClassName}(\"{fieldName}\");");
		sb.AppendLine($"{indent}\t\t_ = configure(nested);");
		sb.AppendLine($"{indent}\t\tMergeNestedFields(nested.GetFields());");
		sb.AppendLine($"{indent}\t\treturn this;");
		sb.AppendLine($"{indent}\t}}");
	}

	private static void EmitNestedBuilderClass(
		StringBuilder sb,
		string indent,
		string builderClassName,
		string propertyName,
		string fieldName,
		NestedTypeModel nestedType
	)
	{
		var nestedBuilderClassName = $"{builderClassName}_{nestedType.TypeName}NestedBuilder";

		sb.AppendLine($"{indent}/// <summary>Builder for configuring nested properties of {nestedType.TypeName}.</summary>");
		sb.AppendLine($"{indent}public sealed class {nestedBuilderClassName}");
		sb.AppendLine($"{indent}{{");
		sb.AppendLine($"{indent}\tprivate readonly string _parentPath;");
		sb.AppendLine($"{indent}\tprivate readonly global::System.Collections.Generic.List<(string Path, global::Elastic.Mapping.Mappings.Definitions.IFieldDefinition Definition)> _fields = [];");
		sb.AppendLine();
		sb.AppendLine($"{indent}\tinternal {nestedBuilderClassName}(string parentPath) => _parentPath = parentPath;");
		sb.AppendLine();

		var nestedProps = nestedType.Properties.Where(p => !p.IsIgnored).ToList();
		for (var i = 0; i < nestedProps.Count; i++)
		{
			var prop = nestedProps[i];
			EmitNestedPropertyMethod(sb, indent, nestedBuilderClassName, prop);

			if (i < nestedProps.Count - 1)
				sb.AppendLine();
		}

		sb.AppendLine();
		sb.AppendLine($"{indent}\tinternal global::System.Collections.Generic.IReadOnlyList<(string Path, global::Elastic.Mapping.Mappings.Definitions.IFieldDefinition Definition)> GetFields() => _fields;");
		sb.AppendLine($"{indent}}}");
		sb.AppendLine();
	}

	private static void EmitNestedPropertyMethod(
		StringBuilder sb,
		string indent,
		string nestedBuilderClassName,
		PropertyMappingModel prop
	)
	{
		var inputBuilder = GetBuilderTypeForFieldType(prop.FieldType);
		var factoryMethod = GetFactoryMethodForFieldType(prop.FieldType);

		if (factoryMethod != null)
		{
			sb.AppendLine($"{indent}\t/// <summary>Configure the {prop.PropertyName} sub-field (maps to \"{{parentPath}}.{prop.FieldName}\").</summary>");
			sb.AppendLine($"{indent}\tpublic {nestedBuilderClassName} {prop.PropertyName}(global::System.Func<{inputBuilder}, {FieldBuilderFqn}> configure)");
			sb.AppendLine($"{indent}\t{{");
			sb.AppendLine($"{indent}\t\tvar fb = new {FieldBuilderFqn}();");
			sb.AppendLine($"{indent}\t\t_ = configure(fb.{factoryMethod}());");
			sb.AppendLine($"{indent}\t\t_fields.Add(($\"{{_parentPath}}.{prop.FieldName}\", fb.GetDefinition()));");
			sb.AppendLine($"{indent}\t\treturn this;");
			sb.AppendLine($"{indent}\t}}");
		}
		else
		{
			sb.AppendLine($"{indent}\t/// <summary>Configure the {prop.PropertyName} sub-field (maps to \"{{parentPath}}.{prop.FieldName}\").</summary>");
			sb.AppendLine($"{indent}\tpublic {nestedBuilderClassName} {prop.PropertyName}(global::System.Func<{FieldBuilderFqn}, {FieldBuilderFqn}> configure)");
			sb.AppendLine($"{indent}\t{{");
			sb.AppendLine($"{indent}\t\tvar fb = new {FieldBuilderFqn}();");
			sb.AppendLine($"{indent}\t\t_ = configure(fb);");
			sb.AppendLine($"{indent}\t\t_fields.Add(($\"{{_parentPath}}.{prop.FieldName}\", fb.GetDefinition()));");
			sb.AppendLine($"{indent}\t\treturn this;");
			sb.AppendLine($"{indent}\t}}");
		}
	}

	private static void EmitPropertyMethod(StringBuilder sb, string indent, string builderClassName, PropertyMappingModel prop)
	{
		var inputBuilder = GetBuilderTypeForFieldType(prop.FieldType);
		var factoryMethod = GetFactoryMethodForFieldType(prop.FieldType);

		if (factoryMethod != null)
		{
			sb.AppendLine($"{indent}\t/// <summary>Configure the {prop.PropertyName} field (maps to \"{prop.FieldName}\").</summary>");
			sb.AppendLine($"{indent}\tpublic {builderClassName} {prop.PropertyName}(global::System.Func<{inputBuilder}, {FieldBuilderFqn}> configure)");
			sb.AppendLine($"{indent}\t{{");
			sb.AppendLine($"{indent}\t\tvar fb = new {FieldBuilderFqn}();");
			sb.AppendLine($"{indent}\t\t_ = configure(fb.{factoryMethod}());");
			sb.AppendLine($"{indent}\t\tAddFieldDirect(\"{prop.FieldName}\", fb.GetDefinition());");
			sb.AppendLine($"{indent}\t\treturn this;");
			sb.AppendLine($"{indent}\t}}");
		}
		else
		{
			sb.AppendLine($"{indent}\t/// <summary>Configure the {prop.PropertyName} field (maps to \"{prop.FieldName}\").</summary>");
			sb.AppendLine($"{indent}\tpublic {builderClassName} {prop.PropertyName}(global::System.Func<{FieldBuilderFqn}, {FieldBuilderFqn}> configure)");
			sb.AppendLine($"{indent}\t{{");
			sb.AppendLine($"{indent}\t\tAddPropertyField(\"{prop.FieldName}\", configure);");
			sb.AppendLine($"{indent}\t\treturn this;");
			sb.AppendLine($"{indent}\t}}");
		}
	}
}
