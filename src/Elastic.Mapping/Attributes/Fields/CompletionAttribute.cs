// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Marks a property as a completion field for autocomplete suggestions.
/// Not auto-inferred; must be explicitly specified.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class CompletionAttribute : Attribute
{
	/// <summary>Analyzer for indexing suggestions.</summary>
	public string? Analyzer { get; init; }

	/// <summary>Analyzer for search queries.</summary>
	public string? SearchAnalyzer { get; init; }
}
