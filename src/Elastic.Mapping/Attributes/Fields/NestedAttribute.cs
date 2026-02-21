// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Marks a property as a nested object type. Nested objects maintain
/// the relationship between their fields, unlike flattened objects.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class NestedAttribute : Attribute
{
	/// <summary>Whether nested documents should be included in parent document queries.</summary>
	public bool IncludeInParent { get; init; }

	/// <summary>Whether nested documents should be included in root document queries.</summary>
	public bool IncludeInRoot { get; init; }
}
