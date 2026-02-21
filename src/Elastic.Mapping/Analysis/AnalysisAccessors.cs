// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping.Analysis;

/// <summary>Base class providing access to built-in Elasticsearch analyzers.</summary>
public class AnalyzersAccessor
{
	/// <summary>Standard analyzer - default for text fields.</summary>
	public string Standard => BuiltInAnalysis.Analyzers.Standard;
	/// <summary>Simple analyzer - divides text at non-letter characters.</summary>
	public string Simple => BuiltInAnalysis.Analyzers.Simple;
	/// <summary>Whitespace analyzer - divides text at whitespace.</summary>
	public string Whitespace => BuiltInAnalysis.Analyzers.Whitespace;
	/// <summary>Stop analyzer - simple analyzer with stop word removal.</summary>
	public string Stop => BuiltInAnalysis.Analyzers.Stop;
	/// <summary>Keyword analyzer - noop analyzer that returns the entire input as a single token.</summary>
	public string Keyword => BuiltInAnalysis.Analyzers.Keyword;
	/// <summary>Pattern analyzer - uses a regular expression to split text into terms.</summary>
	public string Pattern => BuiltInAnalysis.Analyzers.Pattern;
	/// <summary>Fingerprint analyzer - creates a fingerprint for duplicate detection.</summary>
	public string Fingerprint => BuiltInAnalysis.Analyzers.Fingerprint;

	/// <summary>Language-specific analyzers.</summary>
	public LanguageAnalyzers Language { get; } = new();

	/// <summary>Language-specific analyzers.</summary>
	public class LanguageAnalyzers
	{
		public string Arabic => BuiltInAnalysis.Analyzers.Language.Arabic;
		public string Armenian => BuiltInAnalysis.Analyzers.Language.Armenian;
		public string Basque => BuiltInAnalysis.Analyzers.Language.Basque;
		public string Bengali => BuiltInAnalysis.Analyzers.Language.Bengali;
		public string Brazilian => BuiltInAnalysis.Analyzers.Language.Brazilian;
		public string Bulgarian => BuiltInAnalysis.Analyzers.Language.Bulgarian;
		public string Catalan => BuiltInAnalysis.Analyzers.Language.Catalan;
		public string Cjk => BuiltInAnalysis.Analyzers.Language.Cjk;
		public string Czech => BuiltInAnalysis.Analyzers.Language.Czech;
		public string Danish => BuiltInAnalysis.Analyzers.Language.Danish;
		public string Dutch => BuiltInAnalysis.Analyzers.Language.Dutch;
		public string English => BuiltInAnalysis.Analyzers.Language.English;
		public string Estonian => BuiltInAnalysis.Analyzers.Language.Estonian;
		public string Finnish => BuiltInAnalysis.Analyzers.Language.Finnish;
		public string French => BuiltInAnalysis.Analyzers.Language.French;
		public string Galician => BuiltInAnalysis.Analyzers.Language.Galician;
		public string German => BuiltInAnalysis.Analyzers.Language.German;
		public string Greek => BuiltInAnalysis.Analyzers.Language.Greek;
		public string Hindi => BuiltInAnalysis.Analyzers.Language.Hindi;
		public string Hungarian => BuiltInAnalysis.Analyzers.Language.Hungarian;
		public string Indonesian => BuiltInAnalysis.Analyzers.Language.Indonesian;
		public string Irish => BuiltInAnalysis.Analyzers.Language.Irish;
		public string Italian => BuiltInAnalysis.Analyzers.Language.Italian;
		public string Latvian => BuiltInAnalysis.Analyzers.Language.Latvian;
		public string Lithuanian => BuiltInAnalysis.Analyzers.Language.Lithuanian;
		public string Norwegian => BuiltInAnalysis.Analyzers.Language.Norwegian;
		public string Persian => BuiltInAnalysis.Analyzers.Language.Persian;
		public string Portuguese => BuiltInAnalysis.Analyzers.Language.Portuguese;
		public string Romanian => BuiltInAnalysis.Analyzers.Language.Romanian;
		public string Russian => BuiltInAnalysis.Analyzers.Language.Russian;
		public string Sorani => BuiltInAnalysis.Analyzers.Language.Sorani;
		public string Spanish => BuiltInAnalysis.Analyzers.Language.Spanish;
		public string Swedish => BuiltInAnalysis.Analyzers.Language.Swedish;
		public string Thai => BuiltInAnalysis.Analyzers.Language.Thai;
		public string Turkish => BuiltInAnalysis.Analyzers.Language.Turkish;
	}
}

/// <summary>Base class providing access to built-in Elasticsearch tokenizers.</summary>
public class TokenizersAccessor
{
	public string Standard => BuiltInAnalysis.Tokenizers.Standard;
	public string Letter => BuiltInAnalysis.Tokenizers.Letter;
	public string Lowercase => BuiltInAnalysis.Tokenizers.Lowercase;
	public string Whitespace => BuiltInAnalysis.Tokenizers.Whitespace;
	public string UaxUrlEmail => BuiltInAnalysis.Tokenizers.UaxUrlEmail;
	public string Classic => BuiltInAnalysis.Tokenizers.Classic;
	public string Thai => BuiltInAnalysis.Tokenizers.Thai;
	public string NGram => BuiltInAnalysis.Tokenizers.NGram;
	public string EdgeNGram => BuiltInAnalysis.Tokenizers.EdgeNGram;
	public string Keyword => BuiltInAnalysis.Tokenizers.Keyword;
	public string Pattern => BuiltInAnalysis.Tokenizers.Pattern;
	public string SimplePattern => BuiltInAnalysis.Tokenizers.SimplePattern;
	public string SimplePatternSplit => BuiltInAnalysis.Tokenizers.SimplePatternSplit;
	public string CharGroup => BuiltInAnalysis.Tokenizers.CharGroup;
	public string PathHierarchy => BuiltInAnalysis.Tokenizers.PathHierarchy;
}

/// <summary>Base class providing access to built-in Elasticsearch token filters.</summary>
public class TokenFiltersAccessor
{
	public string Lowercase => BuiltInAnalysis.TokenFilters.Lowercase;
	public string Uppercase => BuiltInAnalysis.TokenFilters.Uppercase;
	public string AsciiFolding => BuiltInAnalysis.TokenFilters.AsciiFolding;
	public string Length => BuiltInAnalysis.TokenFilters.Length;
	public string Truncate => BuiltInAnalysis.TokenFilters.Truncate;
	public string Trim => BuiltInAnalysis.TokenFilters.Trim;
	public string Unique => BuiltInAnalysis.TokenFilters.Unique;
	public string Reverse => BuiltInAnalysis.TokenFilters.Reverse;
	public string Elision => BuiltInAnalysis.TokenFilters.Elision;
	public string Stop => BuiltInAnalysis.TokenFilters.Stop;
	public string KeywordMarker => BuiltInAnalysis.TokenFilters.KeywordMarker;
	public string Snowball => BuiltInAnalysis.TokenFilters.Snowball;
	public string Stemmer => BuiltInAnalysis.TokenFilters.Stemmer;
	public string StemmerOverride => BuiltInAnalysis.TokenFilters.StemmerOverride;
	public string PorterStem => BuiltInAnalysis.TokenFilters.PorterStem;
	public string KStem => BuiltInAnalysis.TokenFilters.KStem;
	public string Synonym => BuiltInAnalysis.TokenFilters.Synonym;
	public string SynonymGraph => BuiltInAnalysis.TokenFilters.SynonymGraph;
	public string NGram => BuiltInAnalysis.TokenFilters.NGram;
	public string EdgeNGram => BuiltInAnalysis.TokenFilters.EdgeNGram;
	public string Shingle => BuiltInAnalysis.TokenFilters.Shingle;
	public string WordDelimiter => BuiltInAnalysis.TokenFilters.WordDelimiter;
	public string WordDelimiterGraph => BuiltInAnalysis.TokenFilters.WordDelimiterGraph;
	public string FlattenGraph => BuiltInAnalysis.TokenFilters.FlattenGraph;
	public string Multiplexer => BuiltInAnalysis.TokenFilters.Multiplexer;
	public string Condition => BuiltInAnalysis.TokenFilters.Condition;
	public string PredicateTokenFilter => BuiltInAnalysis.TokenFilters.PredicateTokenFilter;
	public string RemoveDuplicates => BuiltInAnalysis.TokenFilters.RemoveDuplicates;
	public string Classic => BuiltInAnalysis.TokenFilters.Classic;
	public string Apostrophe => BuiltInAnalysis.TokenFilters.Apostrophe;
	public string DecimalDigit => BuiltInAnalysis.TokenFilters.DecimalDigit;
	public string Fingerprint => BuiltInAnalysis.TokenFilters.Fingerprint;
	public string MinHash => BuiltInAnalysis.TokenFilters.MinHash;
	public string CommonGrams => BuiltInAnalysis.TokenFilters.CommonGrams;
	public string CjkBigram => BuiltInAnalysis.TokenFilters.CjkBigram;
	public string CjkWidth => BuiltInAnalysis.TokenFilters.CjkWidth;
	public string DelimitedPayload => BuiltInAnalysis.TokenFilters.DelimitedPayload;
	public string KeepWords => BuiltInAnalysis.TokenFilters.KeepWords;
	public string KeepTypes => BuiltInAnalysis.TokenFilters.KeepTypes;
	public string PatternCapture => BuiltInAnalysis.TokenFilters.PatternCapture;
	public string PatternReplace => BuiltInAnalysis.TokenFilters.PatternReplace;
	public string Phonetic => BuiltInAnalysis.TokenFilters.Phonetic;
	public string IcuFolding => BuiltInAnalysis.TokenFilters.IcuFolding;
	public string IcuNormalizer => BuiltInAnalysis.TokenFilters.IcuNormalizer;
	public string IcuTransform => BuiltInAnalysis.TokenFilters.IcuTransform;
	public string IcuCollation => BuiltInAnalysis.TokenFilters.IcuCollation;
	public string Hunspell => BuiltInAnalysis.TokenFilters.Hunspell;
}

/// <summary>Base class providing access to built-in Elasticsearch character filters.</summary>
public class CharFiltersAccessor
{
	public string HtmlStrip => BuiltInAnalysis.CharFilters.HtmlStrip;
	public string Mapping => BuiltInAnalysis.CharFilters.Mapping;
	public string PatternReplace => BuiltInAnalysis.CharFilters.PatternReplace;
	public string IcuNormalizer => BuiltInAnalysis.CharFilters.IcuNormalizer;
}

/// <summary>Base class providing access to normalizers (empty base, custom normalizers added by generator).</summary>
public class NormalizersAccessor
{
}
