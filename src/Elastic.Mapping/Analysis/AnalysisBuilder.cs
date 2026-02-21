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
	/// Builds the analysis settings into an immutable AnalysisSettings object.
	/// </summary>
	public AnalysisSettings Build() =>
		new(
			_analyzers.ToDictionary(x => x.Name, x => x.Definition),
			_tokenizers.ToDictionary(x => x.Name, x => x.Definition),
			_tokenFilters.ToDictionary(x => x.Name, x => x.Definition),
			_normalizers.ToDictionary(x => x.Name, x => x.Definition),
			_charFilters.ToDictionary(x => x.Name, x => x.Definition)
		);

	/// <summary>Returns true if any analysis components have been configured.</summary>
	public bool HasConfiguration =>
		_analyzers.Count > 0 ||
		_tokenizers.Count > 0 ||
		_tokenFilters.Count > 0 ||
		_normalizers.Count > 0 ||
		_charFilters.Count > 0;
}
