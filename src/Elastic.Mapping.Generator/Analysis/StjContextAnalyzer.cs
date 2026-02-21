// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Generator.Model;
using Microsoft.CodeAnalysis;

namespace Elastic.Mapping.Generator.Analysis;

/// <summary>
/// Reads <c>[JsonSourceGenerationOptions]</c> from a <c>JsonSerializerContext</c> subclass
/// to extract configuration that influences Elasticsearch mapping generation.
/// </summary>
internal static class StjContextAnalyzer
{
	private const string JsonSourceGenerationOptionsAttribute =
		"System.Text.Json.Serialization.JsonSourceGenerationOptionsAttribute";

	private const string JsonConverterAttribute =
		"System.Text.Json.Serialization.JsonConverterAttribute";

	private const string JsonStringEnumConverterName = "JsonStringEnumConverter";

	private const string JsonSerializableAttribute =
		"System.Text.Json.Serialization.JsonSerializableAttribute";

	/// <summary>
	/// Analyzes a <c>JsonSerializerContext</c> type symbol and extracts STJ configuration.
	/// </summary>
	public static StjContextConfig? Analyze(INamedTypeSymbol? jsonContextSymbol)
	{
		if (jsonContextSymbol == null)
			return null;

		var optionsAttr = jsonContextSymbol.GetAttributes()
			.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == JsonSourceGenerationOptionsAttribute);

		if (optionsAttr == null)
			return StjContextConfig.Default;

		var namingPolicy = NamingPolicy.Unspecified;
		var useStringEnumConverter = false;
		var ignoreCondition = DefaultIgnoreCondition.Never;
		var ignoreReadOnlyProperties = false;

		foreach (var arg in optionsAttr.NamedArguments)
		{
			switch (arg.Key)
			{
				case "PropertyNamingPolicy":
					if (arg.Value.Value is int np)
						namingPolicy = (NamingPolicy)np;
					break;
				case "UseStringEnumConverter":
					if (arg.Value.Value is bool usec)
						useStringEnumConverter = usec;
					break;
				case "DefaultIgnoreCondition":
					if (arg.Value.Value is int dic)
						ignoreCondition = (DefaultIgnoreCondition)dic;
					break;
				case "IgnoreReadOnlyProperties":
					if (arg.Value.Value is bool irop)
						ignoreReadOnlyProperties = irop;
					break;
			}
		}

		return new StjContextConfig(namingPolicy, useStringEnumConverter, ignoreCondition, ignoreReadOnlyProperties);
	}

	/// <summary>
	/// Checks whether an enum type is serialized as a string, checking:
	/// 1. <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c> on the enum type itself
	/// 2. Global <c>UseStringEnumConverter</c> from STJ options
	/// </summary>
	public static bool IsEnumSerializedAsString(ITypeSymbol enumType, StjContextConfig? config)
	{
		// Check [JsonConverter] on the enum type
		if (HasJsonStringEnumConverterAttribute(enumType))
			return true;

		// Fall back to global config
		return config?.UseStringEnumConverter == true;
	}

	/// <summary>
	/// Checks whether a property has <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c>.
	/// </summary>
	public static bool PropertyHasJsonStringEnumConverter(IPropertySymbol property)
	{
		foreach (var attr in property.GetAttributes())
		{
			if (attr.AttributeClass?.ToDisplayString() != JsonConverterAttribute)
				continue;

			if (attr.ConstructorArguments.Length > 0 &&
				attr.ConstructorArguments[0].Value is INamedTypeSymbol converterType &&
				converterType.Name == JsonStringEnumConverterName)
				return true;
		}

		return false;
	}

	/// <summary>
	/// Gets the list of types registered via <c>[JsonSerializable(typeof(T))]</c> on the context.
	/// </summary>
	public static HashSet<string> GetSerializableTypes(INamedTypeSymbol? jsonContextSymbol)
	{
		var result = new HashSet<string>();
		if (jsonContextSymbol == null)
			return result;

		foreach (var attr in jsonContextSymbol.GetAttributes())
		{
			if (attr.AttributeClass?.ToDisplayString() != JsonSerializableAttribute)
				continue;

			if (attr.ConstructorArguments.Length > 0 &&
				attr.ConstructorArguments[0].Value is INamedTypeSymbol typeSymbol)
				result.Add(typeSymbol.ToDisplayString());
		}

		return result;
	}

	private static bool HasJsonStringEnumConverterAttribute(ITypeSymbol type)
	{
		foreach (var attr in type.GetAttributes())
		{
			if (attr.AttributeClass?.ToDisplayString() != JsonConverterAttribute)
				continue;

			if (attr.ConstructorArguments.Length > 0 &&
				attr.ConstructorArguments[0].Value is INamedTypeSymbol converterType &&
				converterType.Name == JsonStringEnumConverterName)
				return true;
		}

		return false;
	}
}
