// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Elastic.Mapping.Generator.Model;
using Microsoft.CodeAnalysis;

namespace Elastic.Mapping.Generator.Analysis;

/// <summary>
/// Analyzes a document type for <c>[AiInput]</c> and <c>[AiField]</c> attributes
/// to build an <see cref="AiEnrichmentModel"/>.
/// </summary>
internal static class AiEnrichmentAnalyzer
{
	private const string AiInputAttributeName = "Elastic.Mapping.AiInputAttribute";
	private const string AiFieldAttributeName = "Elastic.Mapping.AiFieldAttribute";
	private const string JsonPropertyNameAttributeName = "System.Text.Json.Serialization.JsonPropertyNameAttribute";

	public static AiEnrichmentModel? Analyze(
		INamedTypeSymbol documentType,
		string? role,
		string? lookupIndexName,
		string? matchField,
		StjContextConfig? stjConfig, // passed through for field name resolution only
		CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();

		var inputs = ImmutableArray.CreateBuilder<AiInputFieldModel>();
		var outputs = ImmutableArray.CreateBuilder<AiOutputFieldModel>();

		foreach (var member in GetAllProperties(documentType))
		{
			ct.ThrowIfCancellationRequested();

			if (member is not IPropertySymbol property)
				continue;
			if (property.DeclaredAccessibility != Accessibility.Public)
				continue;
			if (property.IsStatic || property.IsIndexer)
				continue;

			var attrs = property.GetAttributes();
			var fieldName = ResolveFieldName(property, attrs, stjConfig);

			foreach (var attr in attrs)
			{
				var attrName = attr.AttributeClass?.ToDisplayString();

				if (attrName == AiInputAttributeName)
				{
					inputs.Add(new AiInputFieldModel(property.Name, fieldName));
				}
				else if (attrName == AiFieldAttributeName)
				{
					var description = attr.ConstructorArguments.Length > 0
						? attr.ConstructorArguments[0].Value as string ?? ""
						: "";

					var minItems = GetNamedArg<int>(attr, "MinItems");
					var maxItems = GetNamedArg<int>(attr, "MaxItems");

					var isArray = IsStringArray(property.Type);
					var promptHash = ComputeHash(description);
					var promptHashFieldName = fieldName + "_ph";

					outputs.Add(new AiOutputFieldModel(
						property.Name,
						fieldName,
						description,
						isArray,
						minItems,
						maxItems,
						promptHash,
						promptHashFieldName
					));
				}
			}
		}

		if (outputs.Count == 0)
			return null;

		var resolvedMatchField = matchField ?? "url";

		return new AiEnrichmentModel(
			documentType.Name,
			documentType.ToDisplayString(),
			role,
			lookupIndexName,
			resolvedMatchField,
			inputs.ToImmutable(),
			outputs.ToImmutable()
		);
	}

	private static IEnumerable<ISymbol> GetAllProperties(INamedTypeSymbol type)
	{
		var current = type;
		while (current != null)
		{
			foreach (var member in current.GetMembers())
				yield return member;
			current = current.BaseType;
		}
	}

	private static string ResolveFieldName(IPropertySymbol property, ImmutableArray<AttributeData> attrs, StjContextConfig? stjConfig) =>
		attrs
			.Where(a => a.AttributeClass?.ToDisplayString() == JsonPropertyNameAttributeName)
			.Select(a => a.ConstructorArguments.FirstOrDefault().Value as string)
			.FirstOrDefault()
		?? TypeAnalyzer.ApplyNamingPolicy(
			property.Name,
			stjConfig?.PropertyNamingPolicy ?? NamingPolicy.Unspecified);

	private static bool IsStringArray(ITypeSymbol type)
	{
		// string[]
		if (type is IArrayTypeSymbol arr)
			return arr.ElementType.SpecialType == SpecialType.System_String;

		// string[]? (nullable)
		if (type is INamedTypeSymbol named
			&& named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
			&& named.TypeArguments.Length > 0)
			return IsStringArray(named.TypeArguments[0]);

		return false;
	}

	private static T GetNamedArg<T>(AttributeData attr, string name, T defaultValue = default!)
	{
		var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == name);
		if (arg.Key == null)
			return defaultValue;
		return arg.Value.Value is T value ? value : defaultValue;
	}

	private static string ComputeHash(string input)
	{
		using var sha = SHA256.Create();
		var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
		return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
	}
}
