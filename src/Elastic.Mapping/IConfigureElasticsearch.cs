// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#if NET8_0_OR_GREATER
using Elastic.Mapping.Analysis;
using Elastic.Mapping.Mappings;

namespace Elastic.Mapping;

/// <summary>
/// Static interface for configuring Elasticsearch analysis and mappings on a domain type.
/// Types implementing this interface can provide ConfigureAnalysis and/or ConfigureMappings
/// methods that the source generator will detect and wire up automatically.
/// </summary>
/// <typeparam name="TBuilder">The type-specific mappings builder.</typeparam>
public interface IConfigureElasticsearch<TBuilder>
	where TBuilder : MappingsBuilderBase<TBuilder>
{
	/// <summary>Configures custom analysis components (analyzers, tokenizers, filters).</summary>
	static virtual AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis;

	/// <summary>Configures custom field mappings, runtime fields, and dynamic templates.</summary>
	static virtual TBuilder ConfigureMappings(TBuilder mappings) => mappings;
}
#endif
