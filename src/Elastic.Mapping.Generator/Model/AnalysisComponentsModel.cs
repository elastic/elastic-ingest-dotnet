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

	/// <summary>
	/// Merges another model's components into this one, deduplicating by value.
	/// Used when multiple registrations share the same analysis anchor and each may
	/// contribute different component names (e.g. base vs extended analysis factory).
	/// </summary>
	public AnalysisComponentsModel Merge(AnalysisComponentsModel other) => new(
		Merge(Analyzers, other.Analyzers),
		Merge(Tokenizers, other.Tokenizers),
		Merge(TokenFilters, other.TokenFilters),
		Merge(CharFilters, other.CharFilters),
		Merge(Normalizers, other.Normalizers)
	);

	private static ImmutableArray<AnalysisComponentModel> Merge(
		ImmutableArray<AnalysisComponentModel> a,
		ImmutableArray<AnalysisComponentModel> b)
	{
		if (b.Length == 0) return a;
		if (a.Length == 0) return b;

		var builder = a.ToBuilder();
		foreach (var item in b)
		{
			if (!builder.Any(x => x.Value == item.Value))
				builder.Add(item);
		}
		return builder.ToImmutable();
	}
}

/// <summary>
/// Represents a single analysis component (analyzer, tokenizer, etc.).
/// </summary>
internal sealed record AnalysisComponentModel(
	string ConstantName,
	string Value
);
