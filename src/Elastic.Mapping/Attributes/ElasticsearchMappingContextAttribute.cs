// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Marks a static partial class as an Elasticsearch mapping context.
/// The source generator will generate resolver classes and metadata for all types
/// registered via <see cref="EntityAttribute{T}"/>.
/// </summary>
/// <remarks>
/// Optionally link a <see cref="System.Text.Json.Serialization.JsonSerializerContext"/>
/// via <see cref="JsonContext"/> to inherit serialization configuration (naming policies,
/// enum handling, ignore conditions).
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ElasticsearchMappingContextAttribute : Attribute
{
	/// <summary>
	/// Optional reference to a System.Text.Json <c>JsonSerializerContext</c> subclass.
	/// When set, the generator reads <c>[JsonSourceGenerationOptions]</c> to infer
	/// naming policies, enum serialization, and ignore conditions.
	/// </summary>
	public Type? JsonContext { get; init; }
}
