// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Registers <typeparamref name="T"/> as an Elasticsearch wired stream within an
/// <see cref="ElasticsearchMappingContextAttribute"/> context.
/// <para>
/// Wired streams send data to the Elasticsearch <c>/logs</c> endpoint. Bootstrap is fully
/// managed by Elasticsearch — no index templates or component templates are created.
/// </para>
/// <para>
/// Like data streams, names follow the <c>{type}-{dataset}-{namespace}</c> convention.
/// When <see cref="Namespace"/> is omitted, it is resolved at runtime from environment variables.
/// </para>
/// </summary>
/// <typeparam name="T">The document type to map to an Elasticsearch wired stream.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class WiredStreamAttribute<T> : Attribute where T : class
{
	/// <summary>
	/// Data stream type component (e.g., <c>"logs"</c>, <c>"metrics"</c>).
	/// <para>
	/// Combined with <see cref="Dataset"/> and <see cref="Namespace"/> to form the full
	/// stream name: <c>{Type}-{Dataset}-{Namespace}</c>.
	/// </para>
	/// </summary>
	public required string Type { get; init; }

	/// <summary>
	/// Dataset identifier (e.g., <c>"nginx.access"</c>, <c>"system.cpu"</c>).
	/// <para>
	/// Combined with <see cref="Type"/> and <see cref="Namespace"/> to form the full
	/// stream name.
	/// </para>
	/// </summary>
	public required string Dataset { get; init; }

	/// <summary>
	/// Namespace for environment isolation (e.g., <c>"production"</c>, <c>"development"</c>).
	/// <para>
	/// When omitted, resolved at runtime from environment variables via
	/// <see cref="ElasticsearchTypeContext.ResolveDefaultNamespace"/>.
	/// </para>
	/// </summary>
	public string? Namespace { get; init; }

	/// <summary>
	/// Optional static class containing <c>ConfigureAnalysis</c> and/or <c>ConfigureMappings</c>
	/// methods that customize mappings.
	/// </summary>
	public Type? Configuration { get; init; }

	/// <summary>
	/// Optional variant suffix for registering multiple wired streams of the same document type
	/// with different configurations.
	/// </summary>
	public string? Variant { get; init; }
}
