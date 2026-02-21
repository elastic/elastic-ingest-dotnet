// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Marks a property as a dense_vector field for embeddings and vector similarity.
/// Not auto-inferred; must be explicitly specified.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DenseVectorAttribute : Attribute
{
	/// <summary>Number of dimensions in the vector. Required.</summary>
	public required int Dims { get; init; }

	/// <summary>Similarity function: "cosine", "dot_product", or "l2_norm".</summary>
	public string? Similarity { get; init; }
}
