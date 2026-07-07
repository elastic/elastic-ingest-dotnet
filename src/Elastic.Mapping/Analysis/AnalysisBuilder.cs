// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Analysis.Builders;
using Elastic.Mapping.Analysis.Definitions;

namespace Elastic.Mapping.Analysis;

/// <summary>
/// Fluent builder for configuring Elasticsearch analysis settings.
/// Models receive this builder and return it - no explicit Build() call in model code.
/// </summary>
public sealed class AnalysisBuilder
{
	private readonly List<(string Name, IAnalyzerDefinition Definition)> _analyzers = [];
	private readonly List<(string Name, ITokenizerDefinition Definition)> _tokenizers = [];
	private readonly List<(string Name, ITokenFilterDefinition Definition)> _tokenFilters = [];
	private readonly List<(string Name, INormalizerDefinition Definition)> _normalizers = [];
	private readonly List<(string Name, ICharFilterDefinition Definition)> _charFilters = [];
	private readonly List<AnalysisSettings> _mergeSources = [];

	/// <summary>Configures a named analyzer.</summary>
	public AnalysisBuilder Analyzer(string name, Func<AnalyzerBuilder, AnalyzerBuilder> configure)
	{
		var builder = new AnalyzerBuilder();
		_ = configure(builder);
		var definition = builder.GetDefinition();
		_analyzers.Add((name, definition));
		return this;
	}

	/// <summary>Configures a named tokenizer.</summary>
	public AnalysisBuilder Tokenizer(string name, Func<TokenizerBuilder, TokenizerBuilder> configure)
	{
		var builder = new TokenizerBuilder();
		_ = configure(builder);
		var definition = builder.GetDefinition();
		_tokenizers.Add((name, definition));
		return this;
	}

	/// <summary>Configures a named token filter.</summary>
	public AnalysisBuilder TokenFilter(string name, Func<TokenFilterBuilder, TokenFilterBuilder> configure)
	{
		var builder = new TokenFilterBuilder();
		_ = configure(builder);
		var definition = builder.GetDefinition();
		_tokenFilters.Add((name, definition));
		return this;
	}

	/// <summary>Configures a named normalizer.</summary>
	public AnalysisBuilder Normalizer(string name, Func<NormalizerBuilder, NormalizerBuilder> configure)
	{
		var builder = new NormalizerBuilder();
		_ = configure(builder);
		var definition = builder.GetDefinition();
		_normalizers.Add((name, definition));
		return this;
	}

	/// <summary>Configures a named char filter.</summary>
	public AnalysisBuilder CharFilter(string name, Func<CharFilterBuilder, CharFilterBuilder> configure)
	{
		var builder = new CharFilterBuilder();
		_ = configure(builder);
		var definition = builder.GetDefinition();
		_charFilters.Add((name, definition));
		return this;
	}

	/// <summary>
	/// Additively merges <typeparamref name="TOther"/>'s analysis components into this builder: any
	/// analyzer/tokenizer/token filter/normalizer/char filter name not already defined on this builder
	/// is added. A name already present — whether from an explicit call above or an earlier
	/// <see cref="Merge{TOther}"/> call — is left untouched and never throws, unlike <see cref="Build"/>'s
	/// normal duplicate-name behavior.
	/// </summary>
	/// <param name="resolver">The generated static mapping resolver for <typeparamref name="TOther"/>
	/// (e.g. <c>SomeContext.SomeOtherDocument</c>).</param>
	public AnalysisBuilder Merge<TOther>(IStaticMappingResolver<TOther> resolver)
		where TOther : class
	{
		var other = new AnalysisBuilder();
		resolver.Context.ConfigureAnalysis?.Invoke(other);
		_mergeSources.Add(other.Build());
		return this;
	}

	/// <summary>
	/// Additively merges analysis components from the given <paramref name="context"/> into this builder.
	/// Useful when the source resolver uses a <c>NameTemplate</c> and you already have a resolved
	/// <see cref="ElasticsearchTypeContext"/> from <c>CreateContext(...)</c>, or when you want to
	/// merge from any context without requiring <see cref="IStaticMappingResolver{T}"/>.
	/// Same conflict semantics: names already present on this builder are left untouched.
	/// </summary>
	public AnalysisBuilder Merge(ElasticsearchTypeContext context)
	{
		var other = new AnalysisBuilder();
		context.ConfigureAnalysis?.Invoke(other);
		_mergeSources.Add(other.Build());
		return this;
	}

	/// <summary>
	/// Builds the analysis settings into an immutable AnalysisSettings object.
	/// </summary>
	public AnalysisSettings Build()
	{
		var analyzers = _analyzers.ToDictionary(x => x.Name, x => x.Definition);
		var tokenizers = _tokenizers.ToDictionary(x => x.Name, x => x.Definition);
		var tokenFilters = _tokenFilters.ToDictionary(x => x.Name, x => x.Definition);
		var normalizers = _normalizers.ToDictionary(x => x.Name, x => x.Definition);
		var charFilters = _charFilters.ToDictionary(x => x.Name, x => x.Definition);

		foreach (var source in _mergeSources)
		{
			AddMissing(analyzers, source.Analyzers);
			AddMissing(tokenizers, source.Tokenizers);
			AddMissing(tokenFilters, source.TokenFilters);
			AddMissing(normalizers, source.Normalizers);
			AddMissing(charFilters, source.CharFilters);
		}

		return new(analyzers, tokenizers, tokenFilters, normalizers, charFilters);

		static void AddMissing<TDefinition>(Dictionary<string, TDefinition> target, IReadOnlyDictionary<string, TDefinition> source)
		{
			foreach (var kvp in source)
			{
				if (!target.ContainsKey(kvp.Key))
					target[kvp.Key] = kvp.Value;
			}
		}
	}

	/// <summary>Returns true if any analysis components have been configured.</summary>
	public bool HasConfiguration =>
		_analyzers.Count > 0 ||
		_tokenizers.Count > 0 ||
		_tokenFilters.Count > 0 ||
		_normalizers.Count > 0 ||
		_charFilters.Count > 0 ||
		_mergeSources.Any(s => s.HasConfiguration);
}
