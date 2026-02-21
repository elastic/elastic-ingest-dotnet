// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Marks a property as a semantic_text field for ELSER/semantic search.
/// Not auto-inferred; must be explicitly specified.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SemanticTextAttribute : Attribute
{
	/// <summary>Inference endpoint ID for the semantic model.</summary>
	public string? InferenceId { get; init; }
}
