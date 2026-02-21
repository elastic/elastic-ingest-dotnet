// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Immutable;

namespace Elastic.Mapping.Generator.Model;

/// <summary>
/// Represents all analysis components discovered from the ConfigureAnalysis method.
/// </summary>
internal sealed record AnalysisComponentsModel(
	ImmutableArray<AnalysisComponentModel> Analyzers,
	ImmutableArray<AnalysisComponentModel> Tokenizers,
	ImmutableArray<AnalysisComponentModel> TokenFilters,
	ImmutableArray<AnalysisComponentModel> CharFilters,
	ImmutableArray<AnalysisComponentModel> Normalizers
)
{
	public static AnalysisComponentsModel Empty { get; } = new(
		ImmutableArray<AnalysisComponentModel>.Empty,
		ImmutableArray<AnalysisComponentModel>.Empty,
		ImmutableArray<AnalysisComponentModel>.Empty,
		ImmutableArray<AnalysisComponentModel>.Empty,
		ImmutableArray<AnalysisComponentModel>.Empty
	);

	public bool HasAnyComponents =>
		Analyzers.Length > 0 ||
		Tokenizers.Length > 0 ||
		TokenFilters.Length > 0 ||
		CharFilters.Length > 0 ||
		Normalizers.Length > 0;
}

/// <summary>
/// Represents a single analysis component (analyzer, tokenizer, etc.).
/// </summary>
internal sealed record AnalysisComponentModel(
	string ConstantName,
	string Value
);
