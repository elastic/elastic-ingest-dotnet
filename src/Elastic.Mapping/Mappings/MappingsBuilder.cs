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
	public new MappingsBuilder<TDocument> AddFieldDirect(string path, IFieldDefinition definition) =>
		base.AddFieldDirect(path, definition);

	/// <inheritdoc cref="MappingsBuilderBase{TSelf}.MergeNestedFields"/>
	public new void MergeNestedFields(IReadOnlyList<(string Path, IFieldDefinition Definition)> fields) =>
		base.MergeNestedFields(fields);
}
