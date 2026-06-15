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
	/// <param name="emittedNestedBuilders">
	/// Tracks nested builder class names already emitted in this namespace to avoid
	/// duplicate definitions when multiple document types reference the same nested type.
	/// </param>
	public static string EmitForContext(ContextMappingModel context, TypeRegistration reg, HashSet<string> emittedNestedBuilders)
	{
		var sb = new StringBuilder();

		SharedEmitterHelpers.EmitHeader(sb);

		if (!string.IsNullOrEmpty(context.Namespace))
		{
			sb.AppendLine($"namespace {context.Namespace};");
			sb.AppendLine();
		}

		EmitExtensionMethodsClass(sb, reg.TypeModel, reg.TypeFullyQualifiedName, reg.TypeName, reg.AnalysisComponents, emittedNestedBuilders);

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
		AnalysisComponentsModel analysisComponents,
		HashSet<string> globalEmittedNestedBuilders
	)
	{
		var builderType = $"{MappingsBuilderFqn}<global::{typeFqn}>";
		var extensionClassName = $"{resolverName}MappingsExtensions";

		var nestedBuilders = new List<(string PropertyName, string FieldName, NestedTypeModel NestedType)>();

		sb.AppendLine($"/// <summary>Extension methods for configuring {resolverName} mappings on <see cref=\"{MappingsBuilderFqn}{{TDocument}}\"/>.</summary>");
		sb.AppendLine($"public static partial class {extensionClassName}");
		sb.AppendLine("{");

		// Only emit closed extension methods for own properties (DeclaringTypeName == null).
		// Base-type properties are emitted once globally as generic-constrained methods via EmitBaseExtensionsClass.
		var props = model.Properties.Where(p => !p.IsIgnored && p.DeclaringTypeName == null).ToList();
		for (var i = 0; i < props.Count; i++)
		{
			var prop = props[i];
			EmitPropertyExtensionMethod(sb, builderType, prop, null, null);

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

		// Generate nested builder classes — deduplicate across document types
		// within the same namespace to avoid CS0101 when multiple types reference
		// the same nested type (e.g. IndexedProduct used by two documents).
		foreach (var (propertyName, fieldName, nestedType) in nestedBuilders)
		{
			if (globalEmittedNestedBuilders.Add(nestedType.TypeName))
				EmitNestedBuilderClass(sb, resolverName, propertyName, fieldName, nestedType);
		}
	}

	/// <summary>
	/// Emits a single property extension method.
	/// When <paramref name="typeParam"/> is <c>null</c>, emits a closed method for the concrete type
	/// (<c>builderType</c> is already the fully-typed e.g. <c>MappingsBuilder&lt;global::Foo&gt;</c>).
	/// When <paramref name="typeParam"/> is set (e.g. <c>"TDoc"</c>), emits a generic-constrained method
	/// with a <c>where TDoc : global::{constraintFqn}</c> constraint and uses
	/// <c>MappingsBuilder&lt;TDoc&gt;</c> as both receiver and return type.
	/// </summary>
	private static void EmitPropertyExtensionMethod(
		StringBuilder sb,
		string builderType,
		PropertyMappingModel prop,
		string? typeParam,
		string? constraintFqn
	)
	{
		var inputBuilder = GetBuilderTypeForFieldType(prop.FieldType);
		var factoryMethod = GetFactoryMethodForFieldType(prop.FieldType);

		// For generic form: receiver/return is MappingsBuilder<TDoc> with constraint
		var effectiveBuilderType = typeParam != null
			? $"{MappingsBuilderFqn}<{typeParam}>"
			: builderType;
		var genericSuffix = typeParam != null ? $"<{typeParam}>" : string.Empty;
		var constraintClause = typeParam != null ? $" where {typeParam} : global::{constraintFqn}" : string.Empty;

		if (factoryMethod != null)
		{
			sb.AppendLine($"\t/// <summary>Configure the {prop.PropertyName} field (maps to \"{prop.FieldName}\").</summary>");
			sb.AppendLine($"\tpublic static {effectiveBuilderType} {prop.PropertyName}{genericSuffix}(this {effectiveBuilderType} self, global::System.Func<{inputBuilder}, {FieldBuilderFqn}> configure){constraintClause}");
			sb.AppendLine("\t{");
			sb.AppendLine($"\t\tself.AddFieldDirect(\"{prop.FieldName}\", fb => configure(fb.{factoryMethod}()));");
			sb.AppendLine("\t\treturn self;");
			sb.AppendLine("\t}");
		}
		else
		{
			sb.AppendLine($"\t/// <summary>Configure the {prop.PropertyName} field (maps to \"{prop.FieldName}\").</summary>");
			sb.AppendLine($"\tpublic static {effectiveBuilderType} {prop.PropertyName}{genericSuffix}(this {effectiveBuilderType} self, global::System.Func<{FieldBuilderFqn}, {FieldBuilderFqn}> configure){constraintClause}");
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

	/// <summary>
	/// Emits a standalone file containing generic-constrained extension methods for all properties
	/// declared on a single base type. This ensures that a shared generic helper
	/// <c>MappingsBuilder&lt;T&gt; where T : BaseType</c> can call these methods on any derived type
	/// without ambiguity (no CS0121), because the methods are emitted exactly once per declaring type.
	/// </summary>
	public static string EmitBaseExtensionsClass(
		string emitNamespace,
		string declaringTypeName,
		string declaringTypeFullyQualifiedName,
		IReadOnlyList<PropertyMappingModel> props,
		HashSet<string> emittedNestedBuilders
	)
	{
		var sb = new StringBuilder();

		SharedEmitterHelpers.EmitHeader(sb);

		// Nested builder classes from base types may already be compiled into a referenced assembly
		// (e.g. the base type's project compiled them first). CS0436 is a harmless warning in that
		// scenario — suppress it for the duration of this generated file.
		sb.AppendLine("#pragma warning disable CS0436");
		sb.AppendLine();

		if (!string.IsNullOrEmpty(emitNamespace))
		{
			sb.AppendLine($"namespace {emitNamespace};");
			sb.AppendLine();
		}

		const string typeParam = "TDoc";
		var extensionClassName = $"{declaringTypeName}MappingsExtensions";

		var nestedBuilders = new List<(string PropertyName, string FieldName, NestedTypeModel NestedType)>();

		sb.AppendLine($"/// <summary>Generic-constrained extension methods for configuring {declaringTypeName} base-type mappings.</summary>");
		sb.AppendLine($"public static partial class {extensionClassName}");
		sb.AppendLine("{");

		var nonIgnored = props.Where(p => !p.IsIgnored).ToList();
		for (var i = 0; i < nonIgnored.Count; i++)
		{
			var prop = nonIgnored[i];
			EmitPropertyExtensionMethod(sb, builderType: string.Empty /* unused in generic form */, prop, typeParam, declaringTypeFullyQualifiedName);

			if (prop.NestedType != null)
				nestedBuilders.Add((prop.PropertyName, prop.FieldName, prop.NestedType));

			if (i < nonIgnored.Count - 1)
				sb.AppendLine();
		}

		// Emit generic-constrained nested property overloads
		foreach (var (propertyName, fieldName, nestedType) in nestedBuilders)
		{
			sb.AppendLine();
			EmitGenericNestedPropertyOverload(sb, typeParam, declaringTypeFullyQualifiedName, propertyName, fieldName, nestedType);
		}

		sb.AppendLine("}");
		sb.AppendLine();

		// Emit nested builder classes for base-type nested properties — deduplicate via shared set
		foreach (var (propertyName, fieldName, nestedType) in nestedBuilders)
		{
			if (emittedNestedBuilders.Add(nestedType.TypeName))
				EmitNestedBuilderClass(sb, declaringTypeName, propertyName, fieldName, nestedType);
		}

		return sb.ToString();
	}

	private static void EmitGenericNestedPropertyOverload(
		StringBuilder sb,
		string typeParam,
		string constraintFqn,
		string propertyName,
		string fieldName,
		NestedTypeModel nestedType
	)
	{
		var nestedBuilderClassName = $"{nestedType.TypeName}NestedBuilder";
		var effectiveBuilderType = $"{MappingsBuilderFqn}<{typeParam}>";
		var constraintClause = $" where {typeParam} : global::{constraintFqn}";

		sb.AppendLine($"\t/// <summary>Configure nested properties of the {propertyName} field (maps to \"{fieldName}\").</summary>");
		sb.AppendLine($"\tpublic static {effectiveBuilderType} {propertyName}<{typeParam}>(this {effectiveBuilderType} self, global::System.Func<{nestedBuilderClassName}, {nestedBuilderClassName}> configure){constraintClause}");
		sb.AppendLine("\t{");
		sb.AppendLine($"\t\tvar nested = new {nestedBuilderClassName}(\"{fieldName}\");");
		sb.AppendLine("\t\t_ = configure(nested);");
		sb.AppendLine("\t\tself.MergeNestedFields(nested.GetFields());");
		sb.AppendLine("\t\treturn self;");
		sb.AppendLine("\t}");
	}

	/// <summary>
	/// Emits generic-constrained extension methods on <c>MappingsBuilder&lt;TDoc&gt;</c> that expose
	/// analysis-component accessor instances for code constrained on the base type.
	/// Emitted into a <c>partial</c> class so it can coexist with the field-extension class
	/// produced by <see cref="EmitBaseExtensionsClass"/> without CS0101.
	///
	/// Example usage from generic code:
	/// <code>
	/// static MappingsBuilder&lt;T&gt; Use&lt;T&gt;(MappingsBuilder&lt;T&gt; m) where T : SearchDocumentBase
	///     => m.Normalizers().KeywordNormalizer  // "keyword_normalizer"
	/// </code>
	/// </summary>
	public static string EmitBaseAnchoredAnalysisExtensions(
		string emitNamespace,
		string anchorName,
		string anchorFullyQualifiedName,
		AnalysisComponentsModel components)
	{
		var sb = new StringBuilder();

		SharedEmitterHelpers.EmitHeader(sb);

		// The analysis extensions class is partial alongside the field-extensions class.
		// Suppress CS0436 when the base type's project also compiled one.
		sb.AppendLine("#pragma warning disable CS0436");
		sb.AppendLine();

		if (!string.IsNullOrEmpty(emitNamespace))
		{
			sb.AppendLine($"namespace {emitNamespace};");
			sb.AppendLine();
		}

		const string typeParam = "TDoc";
		var constraintClause = $" where {typeParam} : global::{anchorFullyQualifiedName}";
		var builderType = $"{MappingsBuilderFqn}<{typeParam}>";
		var analysisClassName = $"global::{(string.IsNullOrEmpty(emitNamespace) ? "" : emitNamespace + ".")}{anchorName}Analysis";

		var extensionClassName = $"{anchorName}MappingsExtensions";

		sb.AppendLine($"/// <summary>Generic-constrained extension methods that expose analysis-component accessors for <c>{anchorName}</c>-constrained mappings.</summary>");
		sb.AppendLine($"public static partial class {extensionClassName}");
		sb.AppendLine("{");

		// Emit one accessor property per component kind, returning the static instance from the
		// base-anchored {AnchorName}Analysis class so callers get typed, strongly-named keys.
		EmitAnalysisAccessorExtension(sb, typeParam, constraintClause, builderType, analysisClassName, "Analyzers", components.Analyzers.Length > 0);
		sb.AppendLine();
		EmitAnalysisAccessorExtension(sb, typeParam, constraintClause, builderType, analysisClassName, "Tokenizers", components.Tokenizers.Length > 0);
		sb.AppendLine();
		EmitAnalysisAccessorExtension(sb, typeParam, constraintClause, builderType, analysisClassName, "TokenFilters", components.TokenFilters.Length > 0);
		sb.AppendLine();
		EmitAnalysisAccessorExtension(sb, typeParam, constraintClause, builderType, analysisClassName, "CharFilters", components.CharFilters.Length > 0);
		sb.AppendLine();
		EmitAnalysisAccessorExtension(sb, typeParam, constraintClause, builderType, analysisClassName, "Normalizers", components.Normalizers.Length > 0);

		sb.AppendLine("}");
		sb.AppendLine();

		return sb.ToString();
	}

	private static void EmitAnalysisAccessorExtension(
		StringBuilder sb,
		string typeParam,
		string constraintClause,
		string builderType,
		string analysisClassName,
		string kind,
		bool hasCustom)
	{
		var returnType = $"{analysisClassName}.{kind}Accessor";
		sb.AppendLine($"\t/// <summary>Returns the {kind.ToLowerInvariant()} name accessor for {(hasCustom ? "built-in and custom" : "built-in")} {kind.ToLowerInvariant()}.</summary>");
		sb.AppendLine($"\tpublic static {returnType} {kind}<{typeParam}>(this {builderType} _){constraintClause}");
		sb.AppendLine($"\t\t=> {analysisClassName}.{kind};");
	}
}
