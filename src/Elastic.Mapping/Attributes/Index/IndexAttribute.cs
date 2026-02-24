// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Registers <typeparamref name="T"/> as an Elasticsearch index within an
/// <see cref="ElasticsearchMappingContextAttribute"/> context.
/// <para>
/// The source generator produces a resolver class with compile-time mappings, settings,
/// and accessor delegates. Use <see cref="Name"/> for fixed index names or
/// <see cref="NameTemplate"/> for runtime-parameterized names.
/// </para>
/// <para>
/// When <see cref="Name"/> is set, the resolver exposes a static <c>Context</c> property.
/// When <see cref="NameTemplate"/> is set, the resolver exposes a <c>CreateContext(...)</c>
/// factory method whose parameters are derived from the template placeholders.
/// These two modes are mutually exclusive.
/// </para>
/// </summary>
/// <typeparam name="T">The document type to map to an Elasticsearch index.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class IndexAttribute<T> : Attribute where T : class
{
	/// <summary>
	/// Fixed index name (e.g., <c>"products"</c>).
	/// Mutually exclusive with <see cref="NameTemplate"/>.
	/// <para>
	/// When set, the generated resolver exposes a static <c>Context</c> property
	/// with this name as the write target.
	/// </para>
	/// </summary>
	public string? Name { get; init; }

	/// <summary>
	/// Index name template with <c>{placeholder}</c> tokens resolved at runtime
	/// (e.g., <c>"docs-{searchType}-{env}"</c>).
	/// Mutually exclusive with <see cref="Name"/>.
	/// <para>
	/// Custom placeholders become required <see langword="string"/> parameters on the
	/// generated <c>CreateContext(...)</c> method. The well-known placeholders
	/// <c>{env}</c>, <c>{environment}</c>, and <c>{namespace}</c> are treated as optional
	/// parameters resolved from environment variables via
	/// <see cref="ElasticsearchTypeContext.ResolveDefaultNamespace"/> when omitted.
	/// Well-known placeholders are always placed last in the generated method signature.
	/// </para>
	/// </summary>
	public string? NameTemplate { get; init; }

	/// <summary>
	/// Write alias for ILM or manual alias management (e.g., <c>"products-write"</c>).
	/// <para>
	/// When omitted, defaults to the resolved index name. When <see cref="DatePattern"/>
	/// is set, the effective write alias becomes <c>"{name}-latest"</c>.
	/// </para>
	/// </summary>
	public string? WriteAlias { get; init; }

	/// <summary>
	/// Read alias for search operations (e.g., <c>"products-search"</c>).
	/// <para>
	/// When omitted, reads fall back to the write alias.
	/// </para>
	/// </summary>
	public string? ReadAlias { get; init; }

	/// <summary>
	/// Rolling date pattern appended to the resolved index name at ingest time
	/// (e.g., <c>"yyyy.MM.dd.HHmmss"</c> produces <c>"my-index-2026.02.24.120000"</c>).
	/// <para>
	/// The date suffix is always separated by <c>-</c> and appended after the base name.
	/// Per-document timestamps come from the property marked with <see cref="TimestampAttribute"/>;
	/// when <see cref="ElasticsearchTypeContext.IndexPatternUseBatchDate"/> is set, a single
	/// batch timestamp is used instead.
	/// </para>
	/// <para>
	/// When set, the write alias resolves to <c>"{name}-latest"</c> and the search pattern
	/// to <c>"{name}-*"</c> automatically.
	/// </para>
	/// </summary>
	public string? DatePattern { get; init; }

	/// <summary>
	/// Number of primary shards. Set to <c>-1</c> (default) to omit from index settings,
	/// which is required for Elasticsearch Serverless compatibility.
	/// </summary>
	public int Shards { get; init; } = -1;

	/// <summary>
	/// Number of replica shards. Set to <c>-1</c> (default) to omit from index settings,
	/// which is required for Elasticsearch Serverless compatibility.
	/// </summary>
	public int Replicas { get; init; } = -1;

	/// <summary>
	/// Refresh interval for the index (e.g., <c>"1s"</c>, <c>"30s"</c>, <c>"-1"</c> for disabled).
	/// When omitted, Elasticsearch defaults apply.
	/// </summary>
	public string? RefreshInterval { get; init; }

	/// <summary>
	/// Controls dynamic mapping behavior. When <see langword="false"/>, unmapped fields
	/// are silently ignored. Defaults to <see langword="true"/>.
	/// </summary>
	public bool Dynamic { get; init; } = true;

	/// <summary>
	/// Optional static class containing <c>ConfigureAnalysis</c> and/or <c>ConfigureMappings</c>
	/// methods that customize the Elasticsearch index settings and mappings.
	/// <para>
	/// These methods are also discovered on the context class itself (highest priority)
	/// and on the document type (lowest priority).
	/// </para>
	/// </summary>
	public Type? Configuration { get; init; }

	/// <summary>
	/// Optional variant suffix for registering multiple entities of the same document type
	/// with different configurations.
	/// <para>
	/// When set, the resolver property name becomes <c>{TypeName}{Variant}</c>
	/// (e.g., <c>Variant = "Semantic"</c> on <c>KnowledgeArticle</c> produces
	/// <c>MappingContext.KnowledgeArticleSemantic</c>).
	/// </para>
	/// </summary>
	public string? Variant { get; init; }
}
