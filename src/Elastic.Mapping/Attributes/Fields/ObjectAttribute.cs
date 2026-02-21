// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Marks a property as an object type. Objects have their fields flattened
/// and don't maintain the relationship between their fields.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class ObjectAttribute : Attribute
{
	/// <summary>Whether to index the object's contents. Set to false for stored-only data (default: true).</summary>
	public bool Enabled { get; init; } = true;
}
