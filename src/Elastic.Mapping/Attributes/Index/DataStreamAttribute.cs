// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Registers <typeparamref name="T"/> as an Elasticsearch data stream within an
/// <see cref="ElasticsearchMappingContextAttribute"/> context.
/// <para>
/// Data stream names follow the <c>{type}-{dataset}-{namespace}</c> convention.
/// When <see cref="Namespace"/> is omitted, it is resolved at runtime from environment
/// variables (<c>DOTNET_ENVIRONMENT</c> → <c>ASPNETCORE_ENVIRONMENT</c> →
/// <c>ENVIRONMENT</c> → <c>"development"</c>).
/// </para>
/// </summary>
/// <typeparam name="T">The document type to map to an Elasticsearch data stream.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DataStreamAttribute<T> : Attribute where T : class
{
	/// <summary>
	/// Data stream type component (e.g., <c>"logs"</c>, <c>"metrics"</c>, <c>"traces"</c>, <c>"synthetics"</c>).
	/// <para>
	/// Combined with <see cref="Dataset"/> and <see cref="Namespace"/> to form the full
	/// data stream name: <c>{Type}-{Dataset}-{Namespace}</c>.
	/// </para>
	/// </summary>
	public required string Type { get; init; }

	/// <summary>
	/// Dataset identifier (e.g., <c>"nginx.access"</c>, <c>"system.cpu"</c>).
	/// <para>
	/// Combined with <see cref="Type"/> and <see cref="Namespace"/> to form the full
	/// data stream name.
	/// </para>
	/// </summary>
	public required string Dataset { get; init; }

	/// <summary>
	/// Namespace for environment isolation (e.g., <c>"production"</c>, <c>"development"</c>).
	/// <para>
	/// When omitted, resolved at runtime from environment variables via
	/// <see cref="ElasticsearchTypeContext.ResolveDefaultNamespace"/>:
	/// <c>DOTNET_ENVIRONMENT</c> → <c>ASPNETCORE_ENVIRONMENT</c> →
	/// <c>ENVIRONMENT</c> → <c>"development"</c>.
	/// Set explicitly to pin to a specific namespace.
	/// </para>
	/// </summary>
	public string? Namespace { get; init; }

	/// <summary>
	/// Data stream mode for specialized data stream types.
	/// Defaults to <see cref="Mapping.DataStreamMode.Default"/>.
	/// </summary>
	public DataStreamMode DataStreamMode { get; init; } = DataStreamMode.Default;

	/// <summary>
	/// Optional static class containing <c>ConfigureAnalysis</c> and/or <c>ConfigureMappings</c>
	/// methods that customize the Elasticsearch data stream settings and mappings.
	/// <para>
	/// These methods are also discovered on the context class itself (highest priority)
	/// and on the document type (lowest priority).
	/// </para>
	/// </summary>
	public Type? Configuration { get; init; }

	/// <summary>
	/// Optional variant suffix for registering multiple data streams of the same document type
	/// with different configurations.
	/// <para>
	/// When set, the resolver property name becomes <c>{TypeName}{Variant}</c>
	/// (e.g., <c>Variant = "V2"</c> on <c>ServerMetrics</c> produces
	/// <c>MappingContext.ServerMetricsV2</c>).
	/// </para>
	/// </summary>
	public string? Variant { get; init; }
}
