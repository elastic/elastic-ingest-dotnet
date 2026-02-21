// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Marks a POCO as an Elasticsearch entity for attribute-based discovery (without a mapping context).
/// Applied directly to the domain type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EntityAttribute : Attribute
{
	/// <summary>The Elasticsearch target type.</summary>
	public EntityTarget Target { get; init; } = EntityTarget.Index;

	/// <summary>Concrete index or data stream name.</summary>
	public string? Name { get; init; }

	/// <summary>Search pattern for queries (e.g., "logs-*").</summary>
	public string? SearchPattern { get; init; }

	/// <summary>ILM write alias (e.g., "logs-write").</summary>
	public string? WriteAlias { get; init; }

	/// <summary>ILM read alias (e.g., "logs-read").</summary>
	public string? ReadAlias { get; init; }
}

/// <summary>
/// Registers a type as an Elasticsearch entity within an <see cref="ElasticsearchMappingContextAttribute"/> context.
/// Applied to the context class, not the domain type.
/// Replaces the separate <c>[Index&lt;T&gt;]</c> and <c>[DataStream&lt;T&gt;]</c> attributes with a single
/// unified attribute where <see cref="Target"/> controls the indexing strategy.
/// </summary>
/// <typeparam name="T">The domain type to map to an Elasticsearch entity.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class EntityAttribute<T> : Attribute where T : class
{
	/// <summary>
	/// The Elasticsearch target type. Determines how this entity is indexed.
	/// Defaults to <see cref="EntityTarget.Index"/>.
	/// </summary>
	public EntityTarget Target { get; init; } = EntityTarget.Index;

	/// <summary>
	/// Concrete index or data stream name (e.g., "products").
	/// For data streams, if not set, the name is derived from <see cref="Type"/>-<see cref="Dataset"/>-<see cref="Namespace"/>.
	/// </summary>
	public string? Name { get; init; }

	// --- Data stream properties (used when Target is DataStream or WiredStream) ---

	/// <summary>Data stream type (e.g., "logs", "metrics", "traces", "synthetics").</summary>
	public string? Type { get; init; }

	/// <summary>Dataset identifier (e.g., "nginx.access", "system.cpu").</summary>
	public string? Dataset { get; init; }

	/// <summary>
	/// Namespace for environment separation (e.g., "production", "development").
	/// When omitted, the namespace is resolved at runtime from environment variables:
	/// <c>DOTNET_ENVIRONMENT</c> &gt; <c>ASPNETCORE_ENVIRONMENT</c> &gt; <c>ENVIRONMENT</c>,
	/// falling back to <c>"development"</c>. Set explicitly to override this behavior.
	/// </summary>
	public string? Namespace { get; init; }

	/// <summary>
	/// Data stream mode for specialized data stream types.
	/// Only applicable when <see cref="Target"/> is <see cref="EntityTarget.DataStream"/>.
	/// </summary>
	public DataStreamMode DataStreamMode { get; init; } = DataStreamMode.Default;

	// --- Index properties (used when Target is Index) ---

	/// <summary>ILM write alias (e.g., "logs-write").</summary>
	public string? WriteAlias { get; init; }

	/// <summary>ILM read alias (e.g., "logs-read").</summary>
	public string? ReadAlias { get; init; }

	/// <summary>Rolling date pattern (e.g., "yyyy.MM.dd" produces "logs-2025.01.31").</summary>
	public string? DatePattern { get; init; }

	/// <summary>Search pattern for queries (e.g., "logs-*").</summary>
	public string? SearchPattern { get; init; }

	/// <summary>Number of primary shards. Set to -1 (default) to omit for serverless compatibility.</summary>
	public int Shards { get; init; } = -1;

	/// <summary>Number of replica shards. Set to -1 (default) to omit for serverless compatibility.</summary>
	public int Replicas { get; init; } = -1;

	/// <summary>Refresh interval (e.g., "1s", "30s", "-1" for disabled).</summary>
	public string? RefreshInterval { get; init; }

	/// <summary>Dynamic mapping behavior.</summary>
	public bool Dynamic { get; init; } = true;

	/// <summary>Optional static class containing ConfigureAnalysis/ConfigureMappings methods.</summary>
	public Type? Configuration { get; init; }

	/// <summary>
	/// Optional variant suffix for registering multiple entities of the same type with different configurations.
	/// When set, the resolver property name becomes <c>{TypeName}{Variant}</c> (e.g., "KnowledgeArticleSemantic").
	/// </summary>
	public string? Variant { get; init; }
}
