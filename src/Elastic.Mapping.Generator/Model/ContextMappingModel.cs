// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Immutable;

namespace Elastic.Mapping.Generator.Model;

/// <summary>
/// Represents a complete mapping context with all registered types.
/// </summary>
internal sealed record ContextMappingModel(
	string Namespace,
	string ContextTypeName,
	StjContextConfig? StjConfig,
	ImmutableArray<TypeRegistration> TypeRegistrations
);

/// <summary>
/// Represents a single type registration within a context (via [Entity&lt;T&gt;]).
/// </summary>
internal sealed record TypeRegistration(
	string TypeName,
	string TypeFullyQualifiedName,
	TypeMappingModel TypeModel,
	IndexConfigModel? IndexConfig,
	DataStreamConfigModel? DataStreamConfig,
	EntityConfigModel EntityConfig,
	IngestPropertyModel IngestProperties,
	string? ConfigurationClassName,
	string? ConfigureAnalysisReference,
	bool HasConfigureMappings,
	AnalysisComponentsModel AnalysisComponents,
	string? Variant = null
)
{
	/// <summary>
	/// The resolved property/resolver name: TypeName + Variant suffix (if any).
	/// </summary>
	public string ResolverName => string.IsNullOrEmpty(Variant) ? TypeName : $"{TypeName}{Variant}";
}
