// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Analysis.Definitions;

namespace Elastic.Mapping.Analysis.Builders;

/// <summary>Builder for selecting and configuring an analyzer type.</summary>
public sealed class AnalyzerBuilder
{
	private IAnalyzerDefinition? _definition;

	/// <summary>Creates a custom analyzer with configurable tokenizer and filters.</summary>
	public CustomAnalyzerBuilder Custom() => new(this);

	/// <summary>Creates a pattern analyzer.</summary>
	public PatternAnalyzerBuilder Pattern() => new(this);

	/// <summary>Creates a standard analyzer.</summary>
	public StandardAnalyzerBuilder Standard() => new(this);

	/// <summary>Creates a simple analyzer.</summary>
	public AnalyzerBuilder Simple()
	{
		_definition = new SimpleAnalyzerDefinition();
		return this;
	}

	/// <summary>Creates a whitespace analyzer.</summary>
	public AnalyzerBuilder Whitespace()
	{
		_definition = new WhitespaceAnalyzerDefinition();
		return this;
	}

	/// <summary>Creates a keyword analyzer (no tokenization).</summary>
	public AnalyzerBuilder Keyword()
	{
		_definition = new KeywordAnalyzerDefinition();
		return this;
	}

	/// <summary>Creates a language-specific analyzer.</summary>
	public AnalyzerBuilder Language(string language)
	{
		_definition = new LanguageAnalyzerDefinition(language);
		return this;
	}

	/// <summary>Creates a fingerprint analyzer.</summary>
	public FingerprintAnalyzerBuilder Fingerprint() => new(this);

	internal void SetDefinition(IAnalyzerDefinition definition) => _definition = definition;

	internal IAnalyzerDefinition GetDefinition() =>
		_definition ?? throw new InvalidOperationException("No analyzer type was selected. Call Custom(), Pattern(), Standard(), etc.");
}

/// <summary>Builder for custom analyzers.</summary>
public sealed class CustomAnalyzerBuilder
{
	private readonly AnalyzerBuilder _parent;
	private string _tokenizer = "standard";
	private readonly List<string> _filters = [];
	private readonly List<string> _charFilters = [];

	internal CustomAnalyzerBuilder(AnalyzerBuilder parent) => _parent = parent;

	/// <summary>Sets the tokenizer for this analyzer.</summary>
	public CustomAnalyzerBuilder Tokenizer(string tokenizer)
	{
		_tokenizer = tokenizer;
		return this;
	}

	/// <summary>Adds a single filter to the analyzer.</summary>
	public CustomAnalyzerBuilder Filter(string filter)
	{
		_filters.Add(filter);
		return this;
	}

	/// <summary>Adds multiple filters to the analyzer.</summary>
	public CustomAnalyzerBuilder Filters(params string[] filters)
	{
		_filters.AddRange(filters);
		return this;
	}

	/// <summary>Adds a single char filter to the analyzer.</summary>
	public CustomAnalyzerBuilder CharFilter(string charFilter)
	{
		_charFilters.Add(charFilter);
		return this;
	}

	/// <summary>Adds multiple char filters to the analyzer.</summary>
	public CustomAnalyzerBuilder CharFilters(params string[] charFilters)
	{
		_charFilters.AddRange(charFilters);
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator AnalyzerBuilder(CustomAnalyzerBuilder builder)
	{
		builder._parent.SetDefinition(new CustomAnalyzerDefinition(
			builder._tokenizer,
			builder._filters.ToList(),
			builder._charFilters.ToList()
		));
		return builder._parent;
	}
}

/// <summary>Builder for pattern analyzers.</summary>
public sealed class PatternAnalyzerBuilder
{
	private readonly AnalyzerBuilder _parent;
	private string _pattern = "\\W+";
	private bool _lowercase = true;
	private string? _stopwords;

	internal PatternAnalyzerBuilder(AnalyzerBuilder parent) => _parent = parent;

	/// <summary>Sets the pattern for tokenization.</summary>
	public PatternAnalyzerBuilder PatternValue(string pattern)
	{
		_pattern = pattern;
		return this;
	}

	/// <summary>Sets whether to lowercase tokens.</summary>
	public PatternAnalyzerBuilder Lowercase(bool lowercase)
	{
		_lowercase = lowercase;
		return this;
	}

	/// <summary>Sets the stopwords configuration.</summary>
	public PatternAnalyzerBuilder Stopwords(string stopwords)
	{
		_stopwords = stopwords;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator AnalyzerBuilder(PatternAnalyzerBuilder builder)
	{
		builder._parent.SetDefinition(new PatternAnalyzerDefinition(
			builder._pattern,
			builder._lowercase,
			builder._stopwords
		));
		return builder._parent;
	}
}

/// <summary>Builder for standard analyzers.</summary>
public sealed class StandardAnalyzerBuilder
{
	private readonly AnalyzerBuilder _parent;
	private string? _stopwords;
	private int? _maxTokenLength;

	internal StandardAnalyzerBuilder(AnalyzerBuilder parent) => _parent = parent;

	/// <summary>Sets the stopwords configuration.</summary>
	public StandardAnalyzerBuilder Stopwords(string stopwords)
	{
		_stopwords = stopwords;
		return this;
	}

	/// <summary>Sets the maximum token length.</summary>
	public StandardAnalyzerBuilder MaxTokenLength(int maxTokenLength)
	{
		_maxTokenLength = maxTokenLength;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator AnalyzerBuilder(StandardAnalyzerBuilder builder)
	{
		builder._parent.SetDefinition(new StandardAnalyzerDefinition(
			builder._stopwords,
			builder._maxTokenLength
		));
		return builder._parent;
	}
}

/// <summary>Builder for fingerprint analyzers.</summary>
public sealed class FingerprintAnalyzerBuilder
{
	private readonly AnalyzerBuilder _parent;
	private char? _separator;
	private int? _maxOutputSize;
	private string? _stopwords;

	internal FingerprintAnalyzerBuilder(AnalyzerBuilder parent) => _parent = parent;

	/// <summary>Sets the separator character.</summary>
	public FingerprintAnalyzerBuilder Separator(char separator)
	{
		_separator = separator;
		return this;
	}

	/// <summary>Sets the maximum output size.</summary>
	public FingerprintAnalyzerBuilder MaxOutputSize(int maxOutputSize)
	{
		_maxOutputSize = maxOutputSize;
		return this;
	}

	/// <summary>Sets the stopwords configuration.</summary>
	public FingerprintAnalyzerBuilder Stopwords(string stopwords)
	{
		_stopwords = stopwords;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator AnalyzerBuilder(FingerprintAnalyzerBuilder builder)
	{
		builder._parent.SetDefinition(new FingerprintAnalyzerDefinition(
			builder._separator,
			builder._maxOutputSize,
			builder._stopwords
		));
		return builder._parent;
	}
}
