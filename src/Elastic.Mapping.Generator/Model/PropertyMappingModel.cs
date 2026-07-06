// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Immutable;

namespace Elastic.Mapping.Generator.Model;

/// <summary>
/// Represents information about a nested or object type for recursive mapping.
/// </summary>
internal sealed record NestedTypeModel(
	string TypeName,
	string FullyQualifiedName,
	ImmutableArray<PropertyMappingModel> Properties
)
{
	/// <summary>
	/// Merges this model with another analysis of the same CLR type, unioning properties by name.
	/// Two contexts can analyze the same nested type differently (e.g. differing per-context
	/// <c>DefaultIgnoreCondition</c>), so the emitted nested-builder class must expose the union
	/// of every property ever observed as NOT ignored for this type name — not just whichever
	/// analysis "won" a global dedup race. When a property is ignored in one analysis but not
	/// another, the non-ignored version wins so the property still gets a builder method.
	/// </summary>
	public NestedTypeModel MergeWith(NestedTypeModel other)
	{
		var byName = new Dictionary<string, PropertyMappingModel>(StringComparer.Ordinal);
		foreach (var prop in Properties)
			byName[prop.PropertyName] = prop;
		foreach (var prop in other.Properties)
		{
			if (!byName.TryGetValue(prop.PropertyName, out var existing) || (existing.IsIgnored && !prop.IsIgnored))
				byName[prop.PropertyName] = prop;
		}

		return this with { Properties = byName.Values.ToImmutableArray() };
	}
};

/// <summary>
/// Represents a property's mapping information extracted from source.
/// Must be equatable for incremental generator caching.
/// </summary>
internal sealed record PropertyMappingModel(
	string PropertyName,
	string FieldName,
	string FieldType,
	bool IsIgnored,
	ImmutableDictionary<string, string?> Options,
	NestedTypeModel? NestedType = null,
	/// <summary>
	/// Simple name of the type that declares this property (e.g. <c>SearchDocumentBase</c>).
	/// <c>null</c> means the property is declared on the registered concrete type itself ("own property").
	/// </summary>
	string? DeclaringTypeName = null,
	/// <summary>Namespace of the declaring type (e.g. <c>Elastic.Internal.Search</c>). <c>null</c> for own properties.</summary>
	string? DeclaringTypeNamespace = null,
	/// <summary>Full display string of the declaring type (e.g. <c>Elastic.Internal.Search.SearchDocumentBase</c>). <c>null</c> for own properties.</summary>
	string? DeclaringTypeFullyQualifiedName = null
)
{
	public static PropertyMappingModel Create(
		string propertyName,
		string fieldName,
		string fieldType,
		bool isIgnored = false,
		ImmutableDictionary<string, string?>? options = null,
		NestedTypeModel? nestedType = null,
		string? declaringTypeName = null,
		string? declaringTypeNamespace = null,
		string? declaringTypeFullyQualifiedName = null
	) =>
		new(propertyName, fieldName, fieldType, isIgnored, options ?? ImmutableDictionary<string, string?>.Empty, nestedType,
			declaringTypeName, declaringTypeNamespace, declaringTypeFullyQualifiedName);
}

/// <summary>
/// Elasticsearch field type names.
/// </summary>
internal static class FieldTypes
{
	/// <summary>Returns true if the field type is object-like (object or nested).</summary>
	public static bool IsObjectLike(string fieldType) =>
		fieldType == Object || fieldType == Nested;
	public const string Keyword = "keyword";
	public const string Text = "text";
	public const string Long = "long";
	public const string Integer = "integer";
	public const string Short = "short";
	public const string Byte = "byte";
	public const string Double = "double";
	public const string Float = "float";
	public const string HalfFloat = "half_float";
	public const string ScaledFloat = "scaled_float";
	public const string Date = "date";
	public const string Boolean = "boolean";
	public const string Object = "object";
	public const string Nested = "nested";
	public const string Ip = "ip";
	public const string GeoPoint = "geo_point";
	public const string GeoShape = "geo_shape";
	public const string Completion = "completion";
	public const string DenseVector = "dense_vector";
	public const string SemanticText = "semantic_text";
}
