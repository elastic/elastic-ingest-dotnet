// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Configures a keyword field for exact-match searching.
/// String properties default to text; use this attribute to map a string as a pure keyword field without a text mapping.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class KeywordAttribute : Attribute
{
	/// <summary>Normalizer for case-insensitive matching.</summary>
	public string? Normalizer { get; init; }

	/// <summary>Maximum length for indexing. Longer values are not indexed but still stored. Use 0 for no limit.</summary>
	public int IgnoreAbove { get; init; }

	/// <summary>Whether to store doc values for sorting/aggregations. Set to false to disable (default: true).</summary>
	public bool DocValues { get; init; } = true;

	/// <summary>Whether the field is searchable. Set to false to disable (default: true).</summary>
	public bool Index { get; init; } = true;
}
