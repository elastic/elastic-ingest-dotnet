// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text;
using Elastic.Mapping.Generator.Model;

namespace Elastic.Mapping.Generator.Emitters;

/// <summary>
/// Emits extension methods on <c>MappingsBuilder&lt;TDocument&gt;</c> for strongly-typed mapping configuration.
/// </summary>
internal static class MappingsBuilderEmitter
{
	private const string MappingsBuilderFqn = "global::Elastic.Mapping.Mappings.MappingsBuilder";
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
	/// Emits extension methods on MappingsBuilder&lt;T&gt; for a type registration within a context.
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

		EmitExtensionMethodsClass(sb, reg.TypeModel, reg.TypeFullyQualifiedName, reg.TypeName, reg.AnalysisComponents);

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

	private static void EmitExtensionMethodsClass(
		StringBuilder sb,
		TypeMappingModel model,
		string typeFqn,
		string resolverName,
		AnalysisComponentsModel analysisComponents
	)
	{
		var builderType = $"{MappingsBuilderFqn}<global::{typeFqn}>";
		var extensionClassName = $"{resolverName}MappingsExtensions";

		var nestedBuilders = new List<(string PropertyName, string FieldName, NestedTypeModel NestedType)>();

		sb.AppendLine($"/// <summary>Extension methods for configuring {resolverName} mappings on <see cref=\"{MappingsBuilderFqn}{{TDocument}}\"/>.</summary>");
		sb.AppendLine($"public static class {extensionClassName}");
		sb.AppendLine("{");

		var props = model.Properties.Where(p => !p.IsIgnored).ToList();
		for (var i = 0; i < props.Count; i++)
		{
			var prop = props[i];
			EmitPropertyExtensionMethod(sb, builderType, prop);

			if (prop.NestedType != null)
				nestedBuilders.Add((prop.PropertyName, prop.FieldName, prop.NestedType));

			if (i < props.Count - 1)
				sb.AppendLine();
		}

		foreach (var (propertyName, fieldName, nestedType) in nestedBuilders)
		{
			sb.AppendLine();
			EmitNestedPropertyOverload(sb, builderType, propertyName, fieldName, nestedType);
		}

		sb.AppendLine("}");
		sb.AppendLine();

		// Generate nested builder classes (these remain as helper classes, not extensions)
		var emittedNestedBuilders = new HashSet<string>();
		foreach (var (propertyName, fieldName, nestedType) in nestedBuilders)
		{
			if (emittedNestedBuilders.Add(nestedType.TypeName))
				EmitNestedBuilderClass(sb, resolverName, propertyName, fieldName, nestedType);
		}
	}

	private static void EmitPropertyExtensionMethod(StringBuilder sb, string builderType, PropertyMappingModel prop)
	{
		var inputBuilder = GetBuilderTypeForFieldType(prop.FieldType);
		var factoryMethod = GetFactoryMethodForFieldType(prop.FieldType);

		if (factoryMethod != null)
		{
			sb.AppendLine($"\t/// <summary>Configure the {prop.PropertyName} field (maps to \"{prop.FieldName}\").</summary>");
			sb.AppendLine($"\tpublic static {builderType} {prop.PropertyName}(this {builderType} self, global::System.Func<{inputBuilder}, {FieldBuilderFqn}> configure)");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\tvar fb = new {FieldBuilderFqn}();");
			sb.AppendLine($"\t\t_ = configure(fb.{factoryMethod}());");
			sb.AppendLine($"\t\tself.AddFieldDirect(\"{prop.FieldName}\", fb.GetDefinition());");
			sb.AppendLine("\t\treturn self;");
			sb.AppendLine("\t}");
		}
		else
		{
			sb.AppendLine($"\t/// <summary>Configure the {prop.PropertyName} field (maps to \"{prop.FieldName}\").</summary>");
			sb.AppendLine($"\tpublic static {builderType} {prop.PropertyName}(this {builderType} self, global::System.Func<{FieldBuilderFqn}, {FieldBuilderFqn}> configure)");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\tself.AddPropertyField(\"{prop.FieldName}\", configure);");
			sb.AppendLine("\t\treturn self;");
			sb.AppendLine("\t}");
		}
	}

	private static void EmitNestedPropertyOverload(
		StringBuilder sb,
		string builderType,
		string propertyName,
		string fieldName,
		NestedTypeModel nestedType
	)
	{
		var nestedBuilderClassName = $"{nestedType.TypeName}NestedBuilder";

		sb.AppendLine($"\t/// <summary>Configure nested properties of the {propertyName} field (maps to \"{fieldName}\").</summary>");
		sb.AppendLine($"\tpublic static {builderType} {propertyName}(this {builderType} self, global::System.Func<{nestedBuilderClassName}, {nestedBuilderClassName}> configure)");
		sb.AppendLine("\t{");
		sb.AppendLine($"\t\tvar nested = new {nestedBuilderClassName}(\"{fieldName}\");");
		sb.AppendLine("\t\t_ = configure(nested);");
		sb.AppendLine("\t\tself.MergeNestedFields(nested.GetFields());");
		sb.AppendLine("\t\treturn self;");
		sb.AppendLine("\t}");
	}

	private static void EmitNestedBuilderClass(
		StringBuilder sb,
		string resolverName,
		string propertyName,
		string fieldName,
		NestedTypeModel nestedType
	)
	{
		var nestedBuilderClassName = $"{nestedType.TypeName}NestedBuilder";

		sb.AppendLine($"/// <summary>Builder for configuring nested properties of {nestedType.TypeName}.</summary>");
		sb.AppendLine($"public sealed class {nestedBuilderClassName}");
		sb.AppendLine("{");
		sb.AppendLine("\tprivate readonly string _parentPath;");
		sb.AppendLine($"\tprivate readonly global::System.Collections.Generic.List<(string Path, global::Elastic.Mapping.Mappings.Definitions.IFieldDefinition Definition)> _fields = [];");
		sb.AppendLine();
		sb.AppendLine($"\tinternal {nestedBuilderClassName}(string parentPath) => _parentPath = parentPath;");
		sb.AppendLine();

		var nestedProps = nestedType.Properties.Where(p => !p.IsIgnored).ToList();
		for (var i = 0; i < nestedProps.Count; i++)
		{
			var prop = nestedProps[i];
			EmitNestedPropertyMethod(sb, nestedBuilderClassName, prop);

			if (i < nestedProps.Count - 1)
				sb.AppendLine();
		}

		sb.AppendLine();
		sb.AppendLine($"\tinternal global::System.Collections.Generic.IReadOnlyList<(string Path, global::Elastic.Mapping.Mappings.Definitions.IFieldDefinition Definition)> GetFields() => _fields;");
		sb.AppendLine("}");
		sb.AppendLine();
	}

	private static void EmitNestedPropertyMethod(
		StringBuilder sb,
		string nestedBuilderClassName,
		PropertyMappingModel prop
	)
	{
		var inputBuilder = GetBuilderTypeForFieldType(prop.FieldType);
		var factoryMethod = GetFactoryMethodForFieldType(prop.FieldType);

		if (factoryMethod != null)
		{
			sb.AppendLine($"\t/// <summary>Configure the {prop.PropertyName} sub-field (maps to \"{{parentPath}}.{prop.FieldName}\").</summary>");
			sb.AppendLine($"\tpublic {nestedBuilderClassName} {prop.PropertyName}(global::System.Func<{inputBuilder}, {FieldBuilderFqn}> configure)");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\tvar fb = new {FieldBuilderFqn}();");
			sb.AppendLine($"\t\t_ = configure(fb.{factoryMethod}());");
			sb.AppendLine($"\t\t_fields.Add(($\"{{_parentPath}}.{prop.FieldName}\", fb.GetDefinition()));");
			sb.AppendLine("\t\treturn this;");
			sb.AppendLine("\t}");
		}
		else
		{
			sb.AppendLine($"\t/// <summary>Configure the {prop.PropertyName} sub-field (maps to \"{{parentPath}}.{prop.FieldName}\").</summary>");
			sb.AppendLine($"\tpublic {nestedBuilderClassName} {prop.PropertyName}(global::System.Func<{FieldBuilderFqn}, {FieldBuilderFqn}> configure)");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\tvar fb = new {FieldBuilderFqn}();");
			sb.AppendLine($"\t\t_ = configure(fb);");
			sb.AppendLine($"\t\t_fields.Add(($\"{{_parentPath}}.{prop.FieldName}\", fb.GetDefinition()));");
			sb.AppendLine("\t\treturn this;");
			sb.AppendLine("\t}");
		}
	}
}
