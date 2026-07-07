// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Mappings.Builders;
using Elastic.Mapping.Mappings.Definitions;

namespace Elastic.Mapping.Mappings;

/// <summary>
/// Generic mappings builder parameterized by document type.
/// Property-specific methods are provided via source-generated extension methods.
/// </summary>
/// <typeparam name="TDocument">The document type this builder configures mappings for.</typeparam>
public sealed class MappingsBuilder<TDocument> : MappingsBuilderBase<MappingsBuilder<TDocument>>
	where TDocument : class
{
	/// <inheritdoc cref="MappingsBuilderBase{TSelf}.AddPropertyField"/>
	public new MappingsBuilder<TDocument> AddPropertyField(string path, Func<FieldBuilder, FieldBuilder> configure) =>
		base.AddPropertyField(path, configure);

	/// <inheritdoc cref="MappingsBuilderBase{TSelf}.AddFieldDirect"/>
	public MappingsBuilder<TDocument> AddFieldDirect(string path, Action<FieldBuilder> action) =>
		base.AddFieldDirect(path, action);

	/// <inheritdoc cref="MappingsBuilderBase{TSelf}.MergeNestedFields"/>
	public new void MergeNestedFields(IReadOnlyList<(string Path, IFieldDefinition Definition)> fields) =>
		base.MergeNestedFields(fields);

	/// <summary>
	/// Additively merges <typeparamref name="TOther"/>'s generated mapping into this builder: every
	/// field path present on <typeparamref name="TOther"/> that is not already present on
	/// <typeparamref name="TDocument"/> (via its own generated shape or an explicit call on this
	/// builder) is added. A path that exists on both is left completely untouched —
	/// <typeparamref name="TDocument"/> always wins, this operation never overwrites or reconciles
	/// a conflicting definition for the same path.
	/// </summary>
	/// <param name="resolver">The generated static mapping resolver for <typeparamref name="TOther"/>
	/// (e.g. <c>SomeContext.SomeOtherDocument</c>).</param>
	public MappingsBuilder<TDocument> Merge<TOther>(IStaticMappingResolver<TOther> resolver)
		where TOther : class =>
		base.AddMergeSource(resolver.Context.GetMappingsJson());

	/// <summary>
	/// Additively merges <typeparamref name="TOther"/>'s mapping into this builder, as configured by
	/// <paramref name="configure"/> — the equivalent of merging in an already fully-configured sibling
	/// builder (including its own <c>AddProperty</c>/<c>AddField</c> overrides), not just its bare
	/// generated defaults. Same conflict semantics as <see cref="Merge{TOther}(IStaticMappingResolver{TOther})"/>:
	/// <typeparamref name="TDocument"/> always wins on a path present on both.
	/// </summary>
	public MappingsBuilder<TDocument> Merge<TOther>(
		IStaticMappingResolver<TOther> resolver,
		Func<MappingsBuilder<TOther>, MappingsBuilder<TOther>> configure)
		where TOther : class
	{
		var overrides = configure(new MappingsBuilder<TOther>()).Build();
		var mappingsJson = overrides.MergeIntoMappings(resolver.Context.GetMappingsJson());
		return base.AddMergeSource(mappingsJson);
	}

	/// <summary>
	/// Additively merges mappings from the given <paramref name="context"/> into this builder.
	/// Useful when the source resolver uses a <c>NameTemplate</c> and you already have a resolved
	/// <see cref="ElasticsearchTypeContext"/> from <c>CreateContext(...)</c>, or when you want to
	/// merge from any context without requiring <see cref="IStaticMappingResolver{T}"/>.
	/// Same conflict semantics: <typeparamref name="TDocument"/> always wins on a path present on both.
	/// </summary>
	public MappingsBuilder<TDocument> Merge(ElasticsearchTypeContext context) =>
		base.AddMergeSource(context.GetMappingsJson());

	/// <summary>
	/// Additively merges mappings from the given <paramref name="context"/> into this builder,
	/// as configured by <paramref name="configure"/>. Same conflict semantics:
	/// <typeparamref name="TDocument"/> always wins on a path present on both.
	/// </summary>
	public MappingsBuilder<TDocument> Merge<TOther>(
		ElasticsearchTypeContext context,
		Func<MappingsBuilder<TOther>, MappingsBuilder<TOther>> configure)
		where TOther : class
	{
		var overrides = configure(new MappingsBuilder<TOther>()).Build();
		var mappingsJson = overrides.MergeIntoMappings(context.GetMappingsJson());
		return base.AddMergeSource(mappingsJson);
	}
}
