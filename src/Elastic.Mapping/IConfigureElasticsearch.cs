// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#if NET8_0_OR_GREATER
using System.Collections.Generic;
using Elastic.Mapping.Analysis;
using Elastic.Mapping.Mappings;

namespace Elastic.Mapping;

/// <summary>
/// Interface for configuring Elasticsearch analysis, mappings, and index settings for a document type.
/// Implement this on a configuration class referenced via <c>Configuration = typeof(...)</c> on the
/// <see cref="EntityAttribute{T}"/>, or directly on the entity type as a fallback.
/// Uses default interface methods so implementors only need to override what they customize.
/// </summary>
/// <typeparam name="TDocument">The document type being configured.</typeparam>
public interface IConfigureElasticsearch<TDocument> where TDocument : class
{
	/// <summary>Configures custom analysis components (analyzers, tokenizers, filters).</summary>
	AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis;

	/// <summary>Configures custom field mappings, runtime fields, and dynamic templates.</summary>
	MappingsBuilder<TDocument> ConfigureMappings(MappingsBuilder<TDocument> mappings) => mappings;

	/// <summary>
	/// Additional index settings to include in the settings component template
	/// (e.g. <c>index.default_pipeline</c>).
	/// </summary>
	IReadOnlyDictionary<string, string>? IndexSettings => null;
}
#endif
