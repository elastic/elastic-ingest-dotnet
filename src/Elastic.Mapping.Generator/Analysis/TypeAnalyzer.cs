// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Immutable;
using System.Text;
using Elastic.Mapping.Generator.Model;
using Microsoft.CodeAnalysis;

namespace Elastic.Mapping.Generator.Analysis;

/// <summary>
/// Analyzes type symbols to extract mapping information.
/// </summary>
internal static class TypeAnalyzer
{
	private const string JsonPropertyNameAttributeName = "System.Text.Json.Serialization.JsonPropertyNameAttribute";
	private const string JsonIgnoreAttributeName = "System.Text.Json.Serialization.JsonIgnoreAttribute";

	// Field type attribute names
	private const string TextAttributeName = "Elastic.Mapping.TextAttribute";
	private const string KeywordAttributeName = "Elastic.Mapping.KeywordAttribute";
	private const string DateAttributeName = "Elastic.Mapping.DateAttribute";
	private const string LongAttributeName = "Elastic.Mapping.LongAttribute";
	private const string DoubleAttributeName = "Elastic.Mapping.DoubleAttribute";
	private const string BooleanAttributeName = "Elastic.Mapping.BooleanAttribute";
	private const string NestedAttributeName = "Elastic.Mapping.NestedAttribute";
	private const string ObjectAttributeName = "Elastic.Mapping.ObjectAttribute";
	private const string IpAttributeName = "Elastic.Mapping.IpAttribute";
	private const string GeoPointAttributeName = "Elastic.Mapping.GeoPointAttribute";
	private const string GeoShapeAttributeName = "Elastic.Mapping.GeoShapeAttribute";
	private const string CompletionAttributeName = "Elastic.Mapping.CompletionAttribute";
	private const string DenseVectorAttributeName = "Elastic.Mapping.DenseVectorAttribute";
	private const string SemanticTextAttributeName = "Elastic.Mapping.SemanticTextAttribute";

	/// <summary>
	/// Analyzes a type for mapping, using optional STJ configuration for naming and enum inference.
	/// Used by the context-based pipeline.
	/// </summary>
	public static TypeMappingModel? Analyze(
		INamedTypeSymbol typeSymbol,
		StjContextConfig? stjConfig,
		IndexConfigModel? indexConfig,
		DataStreamConfigModel? dataStreamConfig,
		CancellationToken ct
	)
	{
		ct.ThrowIfCancellationRequested();

		var properties = GetProperties(typeSymbol, stjConfig, ct);

		// Detect Configure* methods on the type itself (fallback for types that still have them)
		var (hasConfigureAnalysis, hasConfigureMappings, mappingsBuilderTypeName) = DetectConfigureMethods(typeSymbol);

		// Parse analysis components if the type has ConfigureAnalysis
		var analysisComponents = hasConfigureAnalysis
			? ConfigureAnalysisParser.Parse(typeSymbol, ct)
			: AnalysisComponentsModel.Empty;

		return new TypeMappingModel(
			typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty,
			typeSymbol.Name,
			false, // isPartial not relevant for context-based types
			indexConfig,
			dataStreamConfig,
			properties,
			ImmutableArray<string>.Empty, // No containing types for context-registered types
			analysisComponents,
			hasConfigureAnalysis,
			hasConfigureMappings,
			mappingsBuilderTypeName
		);
	}

	private static ImmutableArray<PropertyMappingModel> GetProperties(
		INamedTypeSymbol typeSymbol,
		StjContextConfig? stjConfig,
		CancellationToken ct
	) =>
		GetProperties(typeSymbol, stjConfig, [], ct);

	private static ImmutableArray<PropertyMappingModel> GetProperties(
		INamedTypeSymbol typeSymbol,
		StjContextConfig? stjConfig,
		HashSet<string> visitedTypes,
		CancellationToken ct
	)
	{
		var builder = ImmutableArray.CreateBuilder<PropertyMappingModel>();

		foreach (var member in typeSymbol.GetMembers())
		{
			ct.ThrowIfCancellationRequested();

			if (member is not IPropertySymbol property)
				continue;

			if (property.DeclaredAccessibility != Accessibility.Public)
				continue;

			if (property.IsStatic || property.IsIndexer)
				continue;

			// IgnoreReadOnlyProperties: skip properties with no setter
			if (stjConfig?.IgnoreReadOnlyProperties == true && property.SetMethod == null && !property.IsReadOnly)
				continue;
			if (stjConfig?.IgnoreReadOnlyProperties == true && property.IsReadOnly)
				continue;

			var propModel = AnalyzeProperty(property, stjConfig, visitedTypes, ct);
			if (propModel != null)
				builder.Add(propModel);
		}

		return builder.ToImmutable();
	}

	private static PropertyMappingModel? AnalyzeProperty(
		IPropertySymbol property,
		StjContextConfig? stjConfig,
		HashSet<string> visitedTypes,
		CancellationToken ct
	)
	{
		var attrs = property.GetAttributes();

		// Check for [JsonIgnore]
		var isIgnored = attrs.Any(a => a.AttributeClass?.ToDisplayString() == JsonIgnoreAttributeName);

		// Check global DefaultIgnoreCondition.Always
		if (!isIgnored && stjConfig?.IgnoreCondition == DefaultIgnoreCondition.Always)
			isIgnored = true;

		// Get field name from [JsonPropertyName] or apply naming policy
		var fieldName = attrs
			.Where(a => a.AttributeClass?.ToDisplayString() == JsonPropertyNameAttributeName)
			.Select(a => a.ConstructorArguments.FirstOrDefault().Value as string)
			.FirstOrDefault() ?? ApplyNamingPolicy(property.Name, stjConfig?.PropertyNamingPolicy ?? NamingPolicy.Unspecified);

		// Determine field type and options from attributes or CLR type
		var (fieldType, options) = DetermineFieldTypeAndOptions(property, attrs, stjConfig);

		// For nested/object types, recursively analyze the element type
		NestedTypeModel? nestedType = null;
		if (fieldType is FieldTypes.Nested or FieldTypes.Object)
			nestedType = AnalyzeNestedType(property.Type, stjConfig, visitedTypes, ct);

		return PropertyMappingModel.Create(
			property.Name,
			fieldName,
			fieldType,
			isIgnored,
			options,
			nestedType
		);
	}

	private static NestedTypeModel? AnalyzeNestedType(
		ITypeSymbol typeSymbol,
		StjContextConfig? stjConfig,
		HashSet<string> visitedTypes,
		CancellationToken ct
	)
	{
		// Get the element type (unwrap List<T>, T[], etc.)
		var elementType = GetElementType(typeSymbol);
		if (elementType is not INamedTypeSymbol namedType)
			return null;

		var fullyQualifiedName = namedType.ToDisplayString();

		// Prevent circular references
		if (!visitedTypes.Add(fullyQualifiedName))
			return null;

		try
		{
			var properties = GetNestedTypeProperties(namedType, stjConfig, visitedTypes, ct);

			if (properties.Length == 0)
				return null;

			return new NestedTypeModel(namedType.Name, fullyQualifiedName, properties);
		}
		finally
		{
			visitedTypes.Remove(fullyQualifiedName);
		}
	}

	private static ITypeSymbol GetElementType(ITypeSymbol typeSymbol)
	{
		// Handle nullable
		if (typeSymbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
			typeSymbol = nullable.TypeArguments[0];

		// Handle arrays
		if (typeSymbol is IArrayTypeSymbol arrayType)
			return arrayType.ElementType;

		// Handle generic collections (List<T>, IEnumerable<T>, etc.)
		if (typeSymbol is INamedTypeSymbol namedType && namedType.IsGenericType)
		{
			var originalDef = namedType.OriginalDefinition.ToDisplayString();
			if (originalDef.StartsWith("System.Collections.Generic.", StringComparison.Ordinal) ||
				originalDef == "System.Collections.IEnumerable")
			{
				if (namedType.TypeArguments.Length > 0)
					return namedType.TypeArguments[0];
			}
		}

		return typeSymbol;
	}

	private static ImmutableArray<PropertyMappingModel> GetNestedTypeProperties(
		INamedTypeSymbol typeSymbol,
		StjContextConfig? stjConfig,
		HashSet<string> visitedTypes,
		CancellationToken ct
	)
	{
		var builder = ImmutableArray.CreateBuilder<PropertyMappingModel>();

		foreach (var member in typeSymbol.GetMembers())
		{
			ct.ThrowIfCancellationRequested();

			if (member is not IPropertySymbol property)
				continue;

			if (property.DeclaredAccessibility != Accessibility.Public)
				continue;

			if (property.IsStatic || property.IsIndexer)
				continue;

			var propModel = AnalyzeProperty(property, stjConfig, visitedTypes, ct);
			if (propModel != null)
				builder.Add(propModel);
		}

		return builder.ToImmutable();
	}

	private static (string FieldType, ImmutableDictionary<string, string?> Options) DetermineFieldTypeAndOptions(
		IPropertySymbol property,
		ImmutableArray<AttributeData> attrs,
		StjContextConfig? stjConfig)
	{
		var optionsBuilder = ImmutableDictionary.CreateBuilder<string, string?>();

		// Check for explicit type attributes first
		foreach (var attr in attrs)
		{
			var attrName = attr.AttributeClass?.ToDisplayString();

			switch (attrName)
			{
				case TextAttributeName:
					ExtractOptions(attr, optionsBuilder, "Analyzer", "SearchAnalyzer", "Norms", "Index");
					return (FieldTypes.Text, optionsBuilder.ToImmutable());

				case KeywordAttributeName:
					ExtractOptions(attr, optionsBuilder, "Normalizer", "IgnoreAbove", "DocValues", "Index");
					return (FieldTypes.Keyword, optionsBuilder.ToImmutable());

				case DateAttributeName:
					ExtractOptions(attr, optionsBuilder, "Format", "DocValues", "Index");
					return (FieldTypes.Date, optionsBuilder.ToImmutable());

				case LongAttributeName:
					ExtractOptions(attr, optionsBuilder, "DocValues", "Index");
					return (FieldTypes.Long, optionsBuilder.ToImmutable());

				case DoubleAttributeName:
					ExtractOptions(attr, optionsBuilder, "DocValues", "Index");
					return (FieldTypes.Double, optionsBuilder.ToImmutable());

				case BooleanAttributeName:
					ExtractOptions(attr, optionsBuilder, "DocValues", "Index");
					return (FieldTypes.Boolean, optionsBuilder.ToImmutable());

				case NestedAttributeName:
					ExtractOptions(attr, optionsBuilder, "IncludeInParent", "IncludeInRoot");
					return (FieldTypes.Nested, optionsBuilder.ToImmutable());

				case ObjectAttributeName:
					ExtractOptions(attr, optionsBuilder, "Enabled");
					return (FieldTypes.Object, optionsBuilder.ToImmutable());

				case IpAttributeName:
					return (FieldTypes.Ip, optionsBuilder.ToImmutable());

				case GeoPointAttributeName:
					return (FieldTypes.GeoPoint, optionsBuilder.ToImmutable());

				case GeoShapeAttributeName:
					return (FieldTypes.GeoShape, optionsBuilder.ToImmutable());

				case CompletionAttributeName:
					ExtractOptions(attr, optionsBuilder, "Analyzer", "SearchAnalyzer");
					return (FieldTypes.Completion, optionsBuilder.ToImmutable());

				case DenseVectorAttributeName:
					ExtractOptions(attr, optionsBuilder, "Dims", "Similarity");
					return (FieldTypes.DenseVector, optionsBuilder.ToImmutable());

				case SemanticTextAttributeName:
					ExtractOptions(attr, optionsBuilder, "InferenceId");
					return (FieldTypes.SemanticText, optionsBuilder.ToImmutable());
			}
		}

		// Auto-infer from CLR type
		var fieldType = InferFieldType(property.Type, property, stjConfig);
		return (fieldType, optionsBuilder.ToImmutable());
	}

	private static void ExtractOptions(
		AttributeData attr,
		ImmutableDictionary<string, string?>.Builder builder,
		params string[] optionNames)
	{
		foreach (var optionName in optionNames)
		{
			var value = GetNamedArgRaw(attr, optionName);
			if (value != null)
				builder[ToCamelCase(optionName)] = FormatValue(value);
		}
	}

	private static string InferFieldType(ITypeSymbol type, IPropertySymbol? property, StjContextConfig? stjConfig)
	{
		// Unwrap nullable value types (e.g. int?, bool?)
		if (type is INamedTypeSymbol namedType &&
			namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
			type = namedType.TypeArguments[0];

		// Use WithNullableAnnotation to strip nullable reference type annotations (e.g. string? → string)
		var typeName = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();

		return typeName switch
		{
			"string" => FieldTypes.Text,
			"int" or "System.Int32" => FieldTypes.Integer,
			"long" or "System.Int64" => FieldTypes.Long,
			"short" or "System.Int16" => FieldTypes.Short,
			"byte" or "System.Byte" => FieldTypes.Byte,
			"double" or "System.Double" => FieldTypes.Double,
			"float" or "System.Single" => FieldTypes.Float,
			"decimal" or "System.Decimal" => FieldTypes.Double,
			"bool" or "System.Boolean" => FieldTypes.Boolean,
			"System.DateTime" or "System.DateTimeOffset" => FieldTypes.Date,
			"System.Guid" => FieldTypes.Keyword,
			_ when type.TypeKind == TypeKind.Enum => InferEnumFieldType(type, property, stjConfig),
			_ => FieldTypes.Object
		};
	}

	private static string InferEnumFieldType(ITypeSymbol enumType, IPropertySymbol? property, StjContextConfig? stjConfig)
	{
		// Check per-property [JsonConverter(typeof(JsonStringEnumConverter))]
		if (property != null && StjContextAnalyzer.PropertyHasJsonStringEnumConverter(property))
			return FieldTypes.Keyword;

		// Check per-type [JsonConverter(typeof(JsonStringEnumConverter))] or global UseStringEnumConverter
		if (StjContextAnalyzer.IsEnumSerializedAsString(enumType, stjConfig))
			return FieldTypes.Keyword;

		// Default: enum without string converter → keyword (existing behavior, safe default)
		return FieldTypes.Keyword;
	}

	private static object? GetNamedArgRaw(AttributeData attr, string name)
	{
		var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == name);
		return arg.Key != null ? arg.Value.Value : null;
	}

	private static string FormatValue(object value) =>
		value switch
		{
			bool b => b ? "true" : "false",
			string s => $"\"{s}\"",
			_ => value.ToString() ?? string.Empty
		};

	/// <summary>
	/// Applies a naming policy to a property name to derive the ES field name.
	/// </summary>
	internal static string ApplyNamingPolicy(string name, NamingPolicy policy) =>
		policy switch
		{
			NamingPolicy.CamelCase => ToCamelCase(name),
			NamingPolicy.SnakeCaseLower => ToSnakeCaseLower(name),
			NamingPolicy.SnakeCaseUpper => ToSnakeCaseUpper(name),
			NamingPolicy.KebabCaseLower => ToKebabCaseLower(name),
			NamingPolicy.KebabCaseUpper => ToKebabCaseUpper(name),
			_ => ToCamelCase(name) // Default/Unspecified → camelCase (preserve existing behavior)
		};

	internal static string ToCamelCase(string name)
	{
		if (string.IsNullOrEmpty(name))
			return name;

		if (name.Length == 1)
			return name.ToLowerInvariant();

		return char.ToLowerInvariant(name[0]) + name.Substring(1);
	}

	internal static string ToSnakeCaseLower(string name)
	{
		if (string.IsNullOrEmpty(name))
			return name;

		var sb = new StringBuilder();
		for (var i = 0; i < name.Length; i++)
		{
			var c = name[i];
			if (char.IsUpper(c))
			{
				if (i > 0)
					sb.Append('_');
				sb.Append(char.ToLowerInvariant(c));
			}
			else
			{
				sb.Append(c);
			}
		}

		return sb.ToString();
	}

	internal static string ToSnakeCaseUpper(string name) => ToSnakeCaseLower(name).ToUpperInvariant();

	internal static string ToKebabCaseLower(string name)
	{
		if (string.IsNullOrEmpty(name))
			return name;

		var sb = new StringBuilder();
		for (var i = 0; i < name.Length; i++)
		{
			var c = name[i];
			if (char.IsUpper(c))
			{
				if (i > 0)
					sb.Append('-');
				sb.Append(char.ToLowerInvariant(c));
			}
			else
			{
				sb.Append(c);
			}
		}

		return sb.ToString();
	}

	internal static string ToKebabCaseUpper(string name) => ToKebabCaseLower(name).ToUpperInvariant();

	private static (bool HasConfigureAnalysis, bool HasConfigureMappings, string? MappingsBuilderTypeName) DetectConfigureMethods(INamedTypeSymbol typeSymbol)
	{
		var hasConfigureAnalysis = typeSymbol.GetMembers("ConfigureAnalysis")
			.OfType<IMethodSymbol>()
			.Any(m => m.IsStatic && m.Parameters.Length == 1);

		var configureMappingsMethod = typeSymbol.GetMembers("ConfigureMappings")
			.OfType<IMethodSymbol>()
			.FirstOrDefault(m => m.IsStatic && m.Parameters.Length == 1);

		var hasConfigureMappings = configureMappingsMethod != null;
		string? mappingsBuilderTypeName = null;

		if (hasConfigureMappings && configureMappingsMethod != null)
		{
			var parameterType = configureMappingsMethod.Parameters[0].Type;
			mappingsBuilderTypeName = parameterType.Name;
		}

		return (hasConfigureAnalysis, hasConfigureMappings, mappingsBuilderTypeName);
	}
}
