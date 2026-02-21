// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Reflection;

namespace Elastic.Mapping;

/// <summary>Identifies how type metadata was discovered.</summary>
public enum MetadataSource
{
	/// <summary>Metadata was provided by a source-generated mapping context.</summary>
	Context,

	/// <summary>Metadata was discovered via reflection from POCO attributes.</summary>
	Attributes
}

/// <summary>
/// Metadata for a type's field mappings, discovered from generated code or reflection.
/// </summary>
public sealed record TypeFieldMetadata(
	IReadOnlyDictionary<string, string> PropertyToField,
	IReadOnlyCollection<string> IgnoredProperties,
	string? SearchPattern,
	Func<Dictionary<string, PropertyInfo>>? GetPropertyMapFunc,
	MetadataSource Source = MetadataSource.Context,
	IReadOnlyCollection<string>? TextFields = null
);
