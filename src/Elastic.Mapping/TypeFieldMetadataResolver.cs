// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Elastic.Mapping;

/// <summary>
/// Resolves TypeFieldMetadata and field names for types via context or attribute-based discovery.
/// Thread-safe with built-in caching.
/// </summary>
public class TypeFieldMetadataResolver(IElasticsearchMappingContext? context = null)
{
	private readonly ConcurrentDictionary<Type, TypeFieldMetadata?> _typeCache = new();
	private readonly ConcurrentDictionary<MemberInfo, string> _fieldNameCache = new();

	/// <summary>Resolves the ES field name for a C# member.</summary>
	public string Resolve(MemberInfo member) =>
		_fieldNameCache.GetOrAdd(member, ResolveFieldName);

	/// <summary>Gets the search pattern for a type.</summary>
	public string? GetSearchPattern(Type type) =>
		GetTypeMetadata(type)?.SearchPattern;

	/// <summary>Checks if a property should be ignored.</summary>
	public bool IsIgnored(MemberInfo member)
	{
		var metadata = GetTypeMetadata(member.DeclaringType);
		if (metadata != null)
			return metadata.IgnoredProperties.Contains(member.Name);

		return member.GetCustomAttribute<JsonIgnoreAttribute>() != null;
	}

	/// <summary>Gets the generated property map for materialization.</summary>
	public Dictionary<string, PropertyInfo>? GetGeneratedPropertyMap(Type type) =>
		GetTypeMetadata(type)?.GetPropertyMapFunc?.Invoke();

	/// <summary>Checks if a member's field is a text type (requires .keyword for exact-match operations).</summary>
	public bool IsTextField(MemberInfo member)
	{
		var metadata = GetTypeMetadata(member.DeclaringType);
		if (metadata?.TextFields != null)
			return metadata.TextFields.Contains(member.Name);

		// Reflection fallback: string type without [Keyword] attribute
		var memberType = member switch
		{
			PropertyInfo prop => prop.PropertyType,
			FieldInfo field => field.FieldType,
			_ => null
		};

		if (memberType == null)
			return false;

		var underlying = Nullable.GetUnderlyingType(memberType) ?? memberType;
		return underlying == typeof(string) && member.GetCustomAttribute<KeywordAttribute>() == null;
	}

	/// <summary>Gets which resolution path was used for a type.</summary>
	public MetadataSource? GetResolutionSource(Type type) =>
		GetTypeMetadata(type)?.Source;

	/// <summary>Gets resolved metadata for a type.</summary>
	public TypeFieldMetadata? GetTypeMetadata(Type? type)
	{
		if (type == null)
			return null;

		return _typeCache.GetOrAdd(type, ResolveMetadata);
	}

	private TypeFieldMetadata? ResolveMetadata(Type type)
	{
		// 1. Context-based (pre-computed by source generator)
		var fromContext = context?.GetTypeMetadata(type);
		if (fromContext != null)
			return fromContext;

		// 2. Attribute-based discovery
		return DiscoverFromAttributes(type);
	}

	private string ResolveFieldName(MemberInfo member)
	{
		var metadata = GetTypeMetadata(member.DeclaringType);
		if (metadata?.PropertyToField.TryGetValue(member.Name, out var fieldName) == true)
			return fieldName;

		// Fallback for unmapped types
		var jsonAttr = member.GetCustomAttribute<JsonPropertyNameAttribute>();
		return jsonAttr?.Name ?? ToCamelCase(member.Name);
	}

#if NET8_0_OR_GREATER
	[UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Fallback for non-generated types; source generator provides metadata for registered types.")]
#endif
	private static TypeFieldMetadata? DiscoverFromAttributes(Type type)
	{
		var map = new Dictionary<string, string>();
		var ignored = new HashSet<string>();
		var textFields = new HashSet<string>();

		foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (prop.GetCustomAttribute<JsonIgnoreAttribute>() != null)
			{
				_ = ignored.Add(prop.Name);
				continue;
			}

			var jsonName = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
			map[prop.Name] = jsonName?.Name ?? ToCamelCase(prop.Name);

			// String without [Keyword] → text field
			var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
			if (propType == typeof(string) && prop.GetCustomAttribute<KeywordAttribute>() == null)
				_ = textFields.Add(prop.Name);
		}

		if (map.Count == 0)
			return null;

		return new TypeFieldMetadata(map, ignored, DiscoverSearchPattern(type), null, MetadataSource.Attributes, textFields);
	}

	private static string? DiscoverSearchPattern(Type type)
	{
		var entityAttr = type.GetCustomAttribute<EntityAttribute>();
		if (entityAttr != null)
			return entityAttr.SearchPattern ?? entityAttr.ReadAlias;

		return null;
	}

	internal static string ToCamelCase(string name)
	{
		if (string.IsNullOrEmpty(name))
			return name;

		if (name.Length == 1)
			return name.ToLowerInvariant();

		return char.ToLowerInvariant(name[0]) + name[1..];
	}
}
