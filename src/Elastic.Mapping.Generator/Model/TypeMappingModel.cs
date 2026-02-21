// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Immutable;

namespace Elastic.Mapping.Generator.Model;

/// <summary>
/// Represents a type's complete mapping information extracted from source.
/// Must be equatable for incremental generator caching.
/// </summary>
internal sealed record TypeMappingModel(
	string Namespace,
	string TypeName,
	bool IsPartial,
	IndexConfigModel? IndexConfig,
	DataStreamConfigModel? DataStreamConfig,
	ImmutableArray<PropertyMappingModel> Properties,
	ImmutableArray<string> ContainingTypes,
	AnalysisComponentsModel AnalysisComponents,
	bool HasConfigureAnalysis,
	bool HasConfigureMappings,
	string? MappingsBuilderTypeName
)
{
	public string FullTypeName =>
		ContainingTypes.Length > 0
			? $"{string.Join(".", ContainingTypes)}.{TypeName}"
			: TypeName;

	public string FullyQualifiedName =>
		string.IsNullOrEmpty(Namespace)
			? FullTypeName
			: $"{Namespace}.{FullTypeName}";
}

/// <summary>
/// Represents [Entity] attribute configuration for Index targets.
/// </summary>
internal sealed record IndexConfigModel(
	string? Name,
	string? WriteAlias,
	string? ReadAlias,
	string? DatePattern,
	string? SearchPattern,
	int Shards,
	int Replicas,
	string? RefreshInterval,
	bool Dynamic
);

/// <summary>
/// Represents [Entity] attribute configuration for DataStream targets.
/// </summary>
internal sealed record DataStreamConfigModel(
	string Type,
	string Dataset,
	string? Namespace
)
{
	public string? FullName => Namespace != null ? $"{Type}-{Dataset}-{Namespace}" : null;
	public string SearchPattern => $"{Type}-{Dataset}-*";
}

/// <summary>
/// Represents the entity target and data stream mode from [Entity&lt;T&gt;].
/// </summary>
internal sealed record EntityConfigModel(
	string EntityTarget,
	string DataStreamMode
);

/// <summary>
/// Tracks which properties have [Id], [ContentHash], [Timestamp] attributes.
/// </summary>
internal sealed record IngestPropertyModel(
	string? IdPropertyName,
	string? IdPropertyType,
	string? ContentHashPropertyName,
	string? ContentHashPropertyType,
	string? ContentHashFieldName,
	string? TimestampPropertyName,
	string? TimestampPropertyType
);
