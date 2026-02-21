// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Analysis.Definitions;

namespace Elastic.Mapping.Analysis.Builders;

/// <summary>Builder for selecting and configuring a token filter type.</summary>
public sealed class TokenFilterBuilder
{
	private ITokenFilterDefinition? _definition;

	/// <summary>Creates an edge_ngram token filter.</summary>
	public EdgeNGramFilterBuilder EdgeNGram() => new(this);

	/// <summary>Creates a stop token filter.</summary>
	public StopFilterBuilder Stop() => new(this);

	/// <summary>Creates a stemmer token filter.</summary>
	public StemmerFilterBuilder Stemmer() => new(this);

	/// <summary>Creates a shingle token filter.</summary>
	public ShingleFilterBuilder Shingle() => new(this);

	/// <summary>Creates a word_delimiter_graph token filter.</summary>
	public WordDelimiterGraphFilterBuilder WordDelimiterGraph() => new(this);

	/// <summary>Creates a pattern_capture token filter.</summary>
	public PatternCaptureFilterBuilder PatternCapture() => new(this);

	/// <summary>Creates a pattern_replace token filter.</summary>
	public PatternReplaceFilterBuilder PatternReplace() => new(this);

	/// <summary>Creates a synonym token filter.</summary>
	public SynonymFilterBuilder Synonym() => new(this);

	/// <summary>Creates a synonym_graph token filter.</summary>
	public SynonymGraphFilterBuilder SynonymGraph() => new(this);

	/// <summary>Creates a lowercase token filter.</summary>
	public LowercaseFilterBuilder Lowercase() => new(this);

	/// <summary>Creates an uppercase token filter.</summary>
	public TokenFilterBuilder Uppercase()
	{
		_definition = new UppercaseFilterDefinition();
		return this;
	}

	/// <summary>Creates an asciifolding token filter.</summary>
	public AsciiFoldingFilterBuilder AsciiFolding() => new(this);

	/// <summary>Creates a trim token filter.</summary>
	public TokenFilterBuilder Trim()
	{
		_definition = new TrimFilterDefinition();
		return this;
	}

	/// <summary>Creates a truncate token filter.</summary>
	public TruncateFilterBuilder Truncate() => new(this);

	/// <summary>Creates a unique token filter.</summary>
	public UniqueFilterBuilder Unique() => new(this);

	/// <summary>Creates a length token filter.</summary>
	public LengthFilterBuilder Length() => new(this);

	/// <summary>Creates an ngram token filter.</summary>
	public NGramFilterBuilder NGram() => new(this);

	/// <summary>Creates a reverse token filter.</summary>
	public TokenFilterBuilder Reverse()
	{
		_definition = new ReverseFilterDefinition();
		return this;
	}

	/// <summary>Creates an elision token filter.</summary>
	public ElisionFilterBuilder Elision() => new(this);

	/// <summary>Creates a snowball token filter.</summary>
	public TokenFilterBuilder Snowball(string language)
	{
		_definition = new SnowballFilterDefinition(language);
		return this;
	}

	/// <summary>Creates a kstem token filter.</summary>
	public TokenFilterBuilder KStem()
	{
		_definition = new KStemFilterDefinition();
		return this;
	}

	/// <summary>Creates a porter_stem token filter.</summary>
	public TokenFilterBuilder PorterStem()
	{
		_definition = new PorterStemFilterDefinition();
		return this;
	}

	/// <summary>Creates a keep_words token filter.</summary>
	public KeepWordsFilterBuilder KeepWords() => new(this);

	/// <summary>Creates a keep_types token filter.</summary>
	public KeepTypesFilterBuilder KeepTypes() => new(this);

	/// <summary>Creates a multiplexer token filter.</summary>
	public MultiplexerFilterBuilder Multiplexer() => new(this);

	/// <summary>Creates a condition token filter.</summary>
	public ConditionFilterBuilder Condition() => new(this);

	/// <summary>Creates a predicate_token_filter token filter.</summary>
	public TokenFilterBuilder PredicateTokenFilter(string script)
	{
		_definition = new PredicateTokenFilterDefinition(script);
		return this;
	}

	/// <summary>Creates a hunspell token filter.</summary>
	public HunspellFilterBuilder Hunspell() => new(this);

	/// <summary>Creates a common_grams token filter.</summary>
	public CommonGramsFilterBuilder CommonGrams() => new(this);

	internal void SetDefinition(ITokenFilterDefinition definition) => _definition = definition;

	internal ITokenFilterDefinition GetDefinition() =>
		_definition ?? throw new InvalidOperationException("No token filter type was selected. Call EdgeNGram(), Stop(), Stemmer(), etc.");
}

/// <summary>Builder for edge_ngram token filters.</summary>
public sealed class EdgeNGramFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private int _minGram = 1;
	private int _maxGram = 2;
	private bool _preserveOriginal;
	private string _side = "front";

	internal EdgeNGramFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the minimum gram size.</summary>
	public EdgeNGramFilterBuilder MinGram(int minGram)
	{
		_minGram = minGram;
		return this;
	}

	/// <summary>Sets the maximum gram size.</summary>
	public EdgeNGramFilterBuilder MaxGram(int maxGram)
	{
		_maxGram = maxGram;
		return this;
	}

	/// <summary>Sets whether to preserve the original token.</summary>
	public EdgeNGramFilterBuilder PreserveOriginal(bool preserve = true)
	{
		_preserveOriginal = preserve;
		return this;
	}

	/// <summary>Sets the edge side (front or back).</summary>
	public EdgeNGramFilterBuilder Side(string side)
	{
		_side = side;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(EdgeNGramFilterBuilder builder)
	{
		builder._parent.SetDefinition(new EdgeNGramFilterDefinition(
			builder._minGram,
			builder._maxGram,
			builder._preserveOriginal,
			builder._side
		));
		return builder._parent;
	}
}

/// <summary>Builder for stop token filters.</summary>
public sealed class StopFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private string? _stopwords;
	private List<string>? _stopwordsList;
	private bool _ignoreCase;
	private bool _removeTrailing = true;

	internal StopFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the stopwords (e.g., "_english_").</summary>
	public StopFilterBuilder Stopwords(string stopwords)
	{
		_stopwords = stopwords;
		return this;
	}

	/// <summary>Sets the stopwords as a list.</summary>
	public StopFilterBuilder StopwordsList(params string[] stopwords)
	{
		_stopwordsList = [.. stopwords];
		return this;
	}

	/// <summary>Sets whether to ignore case when matching stopwords.</summary>
	public StopFilterBuilder IgnoreCase(bool ignoreCase = true)
	{
		_ignoreCase = ignoreCase;
		return this;
	}

	/// <summary>Sets whether to remove trailing stopwords.</summary>
	public StopFilterBuilder RemoveTrailing(bool removeTrailing)
	{
		_removeTrailing = removeTrailing;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(StopFilterBuilder builder)
	{
		builder._parent.SetDefinition(new StopFilterDefinition(
			builder._stopwords,
			builder._stopwordsList,
			builder._ignoreCase,
			builder._removeTrailing
		));
		return builder._parent;
	}
}

/// <summary>Builder for stemmer token filters.</summary>
public sealed class StemmerFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private string _language = "english";

	internal StemmerFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the language for stemming.</summary>
	public StemmerFilterBuilder Language(string language)
	{
		_language = language;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(StemmerFilterBuilder builder)
	{
		builder._parent.SetDefinition(new StemmerFilterDefinition(builder._language));
		return builder._parent;
	}
}

/// <summary>Builder for shingle token filters.</summary>
public sealed class ShingleFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private int _minShingleSize = 2;
	private int _maxShingleSize = 2;
	private bool _outputUnigrams = true;
	private bool _outputUnigramsIfNoShingles;
	private string _tokenSeparator = " ";
	private string? _fillerToken;

	internal ShingleFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the minimum shingle size.</summary>
	public ShingleFilterBuilder MinShingleSize(int size)
	{
		_minShingleSize = size;
		return this;
	}

	/// <summary>Sets the maximum shingle size.</summary>
	public ShingleFilterBuilder MaxShingleSize(int size)
	{
		_maxShingleSize = size;
		return this;
	}

	/// <summary>Sets whether to output unigrams.</summary>
	public ShingleFilterBuilder OutputUnigrams(bool output)
	{
		_outputUnigrams = output;
		return this;
	}

	/// <summary>Sets whether to output unigrams if no shingles.</summary>
	public ShingleFilterBuilder OutputUnigramsIfNoShingles(bool output = true)
	{
		_outputUnigramsIfNoShingles = output;
		return this;
	}

	/// <summary>Sets the token separator.</summary>
	public ShingleFilterBuilder TokenSeparator(string separator)
	{
		_tokenSeparator = separator;
		return this;
	}

	/// <summary>Sets the filler token.</summary>
	public ShingleFilterBuilder FillerToken(string token)
	{
		_fillerToken = token;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(ShingleFilterBuilder builder)
	{
		builder._parent.SetDefinition(new ShingleFilterDefinition(
			builder._minShingleSize,
			builder._maxShingleSize,
			builder._outputUnigrams,
			builder._outputUnigramsIfNoShingles,
			builder._tokenSeparator,
			builder._fillerToken
		));
		return builder._parent;
	}
}

/// <summary>Builder for word_delimiter_graph token filters.</summary>
public sealed class WordDelimiterGraphFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private bool _preserveOriginal;
	private bool _splitOnCaseChange = true;
	private bool _splitOnNumerics = true;
	private bool _generateWordParts = true;
	private bool _generateNumberParts = true;
	private bool _catenateWords;
	private bool _catenateNumbers;
	private bool _catenateAll;
	private bool _stemEnglishPossessive = true;
	private List<string>? _protectedWords;
	private List<string>? _typeTable;

	internal WordDelimiterGraphFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets whether to preserve the original token.</summary>
	public WordDelimiterGraphFilterBuilder PreserveOriginal(bool preserve = true)
	{
		_preserveOriginal = preserve;
		return this;
	}

	/// <summary>Sets whether to split on case change.</summary>
	public WordDelimiterGraphFilterBuilder SplitOnCaseChange(bool split)
	{
		_splitOnCaseChange = split;
		return this;
	}

	/// <summary>Sets whether to split on numerics.</summary>
	public WordDelimiterGraphFilterBuilder SplitOnNumerics(bool split)
	{
		_splitOnNumerics = split;
		return this;
	}

	/// <summary>Sets whether to generate word parts.</summary>
	public WordDelimiterGraphFilterBuilder GenerateWordParts(bool generate)
	{
		_generateWordParts = generate;
		return this;
	}

	/// <summary>Sets whether to generate number parts.</summary>
	public WordDelimiterGraphFilterBuilder GenerateNumberParts(bool generate)
	{
		_generateNumberParts = generate;
		return this;
	}

	/// <summary>Sets whether to catenate words.</summary>
	public WordDelimiterGraphFilterBuilder CatenateWords(bool catenate = true)
	{
		_catenateWords = catenate;
		return this;
	}

	/// <summary>Sets whether to catenate numbers.</summary>
	public WordDelimiterGraphFilterBuilder CatenateNumbers(bool catenate = true)
	{
		_catenateNumbers = catenate;
		return this;
	}

	/// <summary>Sets whether to catenate all.</summary>
	public WordDelimiterGraphFilterBuilder CatenateAll(bool catenate = true)
	{
		_catenateAll = catenate;
		return this;
	}

	/// <summary>Sets whether to stem English possessives.</summary>
	public WordDelimiterGraphFilterBuilder StemEnglishPossessive(bool stem)
	{
		_stemEnglishPossessive = stem;
		return this;
	}

	/// <summary>Sets the protected words list.</summary>
	public WordDelimiterGraphFilterBuilder ProtectedWords(params string[] words)
	{
		_protectedWords = [.. words];
		return this;
	}

	/// <summary>Sets the type table.</summary>
	public WordDelimiterGraphFilterBuilder TypeTable(params string[] types)
	{
		_typeTable = [.. types];
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(WordDelimiterGraphFilterBuilder builder)
	{
		builder._parent.SetDefinition(new WordDelimiterGraphFilterDefinition(
			builder._preserveOriginal,
			builder._splitOnCaseChange,
			builder._splitOnNumerics,
			builder._generateWordParts,
			builder._generateNumberParts,
			builder._catenateWords,
			builder._catenateNumbers,
			builder._catenateAll,
			builder._stemEnglishPossessive,
			builder._protectedWords,
			builder._typeTable
		));
		return builder._parent;
	}
}

/// <summary>Builder for pattern_capture token filters.</summary>
public sealed class PatternCaptureFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private readonly List<string> _patterns = [];
	private bool _preserveOriginal = true;

	internal PatternCaptureFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Adds a pattern.</summary>
	public PatternCaptureFilterBuilder Pattern(string pattern)
	{
		_patterns.Add(pattern);
		return this;
	}

	/// <summary>Adds multiple patterns.</summary>
	public PatternCaptureFilterBuilder Patterns(params string[] patterns)
	{
		_patterns.AddRange(patterns);
		return this;
	}

	/// <summary>Sets whether to preserve the original token.</summary>
	public PatternCaptureFilterBuilder PreserveOriginal(bool preserve)
	{
		_preserveOriginal = preserve;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(PatternCaptureFilterBuilder builder)
	{
		builder._parent.SetDefinition(new PatternCaptureFilterDefinition(
			builder._patterns.ToList(),
			builder._preserveOriginal
		));
		return builder._parent;
	}
}

/// <summary>Builder for pattern_replace token filters.</summary>
public sealed class PatternReplaceFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private string _pattern = ".*";
	private string? _replacement;
	private string? _flags;
	private bool _all = true;

	internal PatternReplaceFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the pattern.</summary>
	public PatternReplaceFilterBuilder Pattern(string pattern)
	{
		_pattern = pattern;
		return this;
	}

	/// <summary>Sets the replacement string.</summary>
	public PatternReplaceFilterBuilder Replacement(string replacement)
	{
		_replacement = replacement;
		return this;
	}

	/// <summary>Sets the regex flags.</summary>
	public PatternReplaceFilterBuilder Flags(string flags)
	{
		_flags = flags;
		return this;
	}

	/// <summary>Sets whether to replace all occurrences.</summary>
	public PatternReplaceFilterBuilder All(bool all)
	{
		_all = all;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(PatternReplaceFilterBuilder builder)
	{
		builder._parent.SetDefinition(new PatternReplaceFilterDefinition(
			builder._pattern,
			builder._replacement,
			builder._flags,
			builder._all
		));
		return builder._parent;
	}
}

/// <summary>Builder for synonym token filters.</summary>
public sealed class SynonymFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private List<string>? _synonyms;
	private string? _synonymsPath;
	private string? _synonymsSet;
	private bool _expand = true;
	private bool _lenient;
	private bool? _updateable;

	internal SynonymFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the synonyms inline.</summary>
	public SynonymFilterBuilder Synonyms(params string[] synonyms)
	{
		_synonyms = [.. synonyms];
		return this;
	}

	/// <summary>Sets the path to the synonyms file.</summary>
	public SynonymFilterBuilder SynonymsPath(string path)
	{
		_synonymsPath = path;
		return this;
	}

	/// <summary>Sets the synonyms set name (for API-managed synonym sets).</summary>
	public SynonymFilterBuilder SynonymsSet(string synonymsSet)
	{
		_synonymsSet = synonymsSet;
		return this;
	}

	/// <summary>Sets whether to expand synonyms.</summary>
	public SynonymFilterBuilder Expand(bool expand)
	{
		_expand = expand;
		return this;
	}

	/// <summary>Sets whether to be lenient with errors.</summary>
	public SynonymFilterBuilder Lenient(bool lenient = true)
	{
		_lenient = lenient;
		return this;
	}

	/// <summary>Sets whether this filter is updateable at search time.</summary>
	public SynonymFilterBuilder Updateable(bool updateable = true)
	{
		_updateable = updateable;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(SynonymFilterBuilder builder)
	{
		builder._parent.SetDefinition(new SynonymFilterDefinition(
			builder._synonyms,
			builder._synonymsPath,
			builder._synonymsSet,
			builder._expand,
			builder._lenient,
			builder._updateable
		));
		return builder._parent;
	}
}

/// <summary>Builder for synonym_graph token filters.</summary>
public sealed class SynonymGraphFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private List<string>? _synonyms;
	private string? _synonymsPath;
	private string? _synonymsSet;
	private bool _expand = true;
	private bool _lenient;
	private bool? _updateable;

	internal SynonymGraphFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the synonyms inline.</summary>
	public SynonymGraphFilterBuilder Synonyms(params string[] synonyms)
	{
		_synonyms = [.. synonyms];
		return this;
	}

	/// <summary>Sets the path to the synonyms file.</summary>
	public SynonymGraphFilterBuilder SynonymsPath(string path)
	{
		_synonymsPath = path;
		return this;
	}

	/// <summary>Sets the synonyms set name (for API-managed synonym sets).</summary>
	public SynonymGraphFilterBuilder SynonymsSet(string synonymsSet)
	{
		_synonymsSet = synonymsSet;
		return this;
	}

	/// <summary>Sets whether to expand synonyms.</summary>
	public SynonymGraphFilterBuilder Expand(bool expand)
	{
		_expand = expand;
		return this;
	}

	/// <summary>Sets whether to be lenient with errors.</summary>
	public SynonymGraphFilterBuilder Lenient(bool lenient = true)
	{
		_lenient = lenient;
		return this;
	}

	/// <summary>Sets whether this filter is updateable at search time.</summary>
	public SynonymGraphFilterBuilder Updateable(bool updateable = true)
	{
		_updateable = updateable;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(SynonymGraphFilterBuilder builder)
	{
		builder._parent.SetDefinition(new SynonymGraphFilterDefinition(
			builder._synonyms,
			builder._synonymsPath,
			builder._synonymsSet,
			builder._expand,
			builder._lenient,
			builder._updateable
		));
		return builder._parent;
	}
}

/// <summary>Builder for lowercase token filters.</summary>
public sealed class LowercaseFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private string? _language;

	internal LowercaseFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the language for locale-specific lowercasing.</summary>
	public LowercaseFilterBuilder Language(string language)
	{
		_language = language;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(LowercaseFilterBuilder builder)
	{
		builder._parent.SetDefinition(new LowercaseFilterDefinition(builder._language));
		return builder._parent;
	}
}

/// <summary>Builder for asciifolding token filters.</summary>
public sealed class AsciiFoldingFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private bool _preserveOriginal;

	internal AsciiFoldingFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets whether to preserve the original token.</summary>
	public AsciiFoldingFilterBuilder PreserveOriginal(bool preserve = true)
	{
		_preserveOriginal = preserve;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(AsciiFoldingFilterBuilder builder)
	{
		builder._parent.SetDefinition(new AsciiFoldingFilterDefinition(builder._preserveOriginal));
		return builder._parent;
	}
}

/// <summary>Builder for truncate token filters.</summary>
public sealed class TruncateFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private int _length = 10;

	internal TruncateFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the maximum token length.</summary>
	public TruncateFilterBuilder Length(int length)
	{
		_length = length;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(TruncateFilterBuilder builder)
	{
		builder._parent.SetDefinition(new TruncateFilterDefinition(builder._length));
		return builder._parent;
	}
}

/// <summary>Builder for unique token filters.</summary>
public sealed class UniqueFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private bool _onlyOnSamePosition;

	internal UniqueFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets whether to only consider same position.</summary>
	public UniqueFilterBuilder OnlyOnSamePosition(bool only = true)
	{
		_onlyOnSamePosition = only;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(UniqueFilterBuilder builder)
	{
		builder._parent.SetDefinition(new UniqueFilterDefinition(builder._onlyOnSamePosition));
		return builder._parent;
	}
}

/// <summary>Builder for length token filters.</summary>
public sealed class LengthFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private int _min;
	private int _max = int.MaxValue;

	internal LengthFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the minimum token length.</summary>
	public LengthFilterBuilder Min(int min)
	{
		_min = min;
		return this;
	}

	/// <summary>Sets the maximum token length.</summary>
	public LengthFilterBuilder Max(int max)
	{
		_max = max;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(LengthFilterBuilder builder)
	{
		builder._parent.SetDefinition(new LengthFilterDefinition(builder._min, builder._max));
		return builder._parent;
	}
}

/// <summary>Builder for ngram token filters.</summary>
public sealed class NGramFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private int _minGram = 1;
	private int _maxGram = 2;
	private bool _preserveOriginal;

	internal NGramFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the minimum gram size.</summary>
	public NGramFilterBuilder MinGram(int minGram)
	{
		_minGram = minGram;
		return this;
	}

	/// <summary>Sets the maximum gram size.</summary>
	public NGramFilterBuilder MaxGram(int maxGram)
	{
		_maxGram = maxGram;
		return this;
	}

	/// <summary>Sets whether to preserve the original token.</summary>
	public NGramFilterBuilder PreserveOriginal(bool preserve = true)
	{
		_preserveOriginal = preserve;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(NGramFilterBuilder builder)
	{
		builder._parent.SetDefinition(new NGramFilterDefinition(
			builder._minGram,
			builder._maxGram,
			builder._preserveOriginal
		));
		return builder._parent;
	}
}

/// <summary>Builder for elision token filters.</summary>
public sealed class ElisionFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private List<string>? _articles;
	private string? _articlesPath;
	private bool _articlesCase;

	internal ElisionFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the articles to elide.</summary>
	public ElisionFilterBuilder Articles(params string[] articles)
	{
		_articles = [.. articles];
		return this;
	}

	/// <summary>Sets the path to the articles file.</summary>
	public ElisionFilterBuilder ArticlesPath(string path)
	{
		_articlesPath = path;
		return this;
	}

	/// <summary>Sets whether articles matching is case sensitive.</summary>
	public ElisionFilterBuilder ArticlesCase(bool caseSensitive = true)
	{
		_articlesCase = caseSensitive;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(ElisionFilterBuilder builder)
	{
		builder._parent.SetDefinition(new ElisionFilterDefinition(
			builder._articles,
			builder._articlesPath,
			builder._articlesCase
		));
		return builder._parent;
	}
}

/// <summary>Builder for keep_words token filters.</summary>
public sealed class KeepWordsFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private List<string>? _keepWords;
	private string? _keepWordsPath;
	private bool _keepWordsCase;

	internal KeepWordsFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the words to keep.</summary>
	public KeepWordsFilterBuilder KeepWords(params string[] words)
	{
		_keepWords = [.. words];
		return this;
	}

	/// <summary>Sets the path to the keep words file.</summary>
	public KeepWordsFilterBuilder KeepWordsPath(string path)
	{
		_keepWordsPath = path;
		return this;
	}

	/// <summary>Sets whether keep words matching is case sensitive.</summary>
	public KeepWordsFilterBuilder KeepWordsCase(bool caseSensitive = true)
	{
		_keepWordsCase = caseSensitive;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(KeepWordsFilterBuilder builder)
	{
		builder._parent.SetDefinition(new KeepWordsFilterDefinition(
			builder._keepWords,
			builder._keepWordsPath,
			builder._keepWordsCase
		));
		return builder._parent;
	}
}

/// <summary>Builder for keep_types token filters.</summary>
public sealed class KeepTypesFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private readonly List<string> _types = [];
	private string _mode = "include";

	internal KeepTypesFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the types to keep or exclude.</summary>
	public KeepTypesFilterBuilder Types(params string[] types)
	{
		_types.AddRange(types);
		return this;
	}

	/// <summary>Sets the mode (include or exclude).</summary>
	public KeepTypesFilterBuilder Mode(string mode)
	{
		_mode = mode;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(KeepTypesFilterBuilder builder)
	{
		builder._parent.SetDefinition(new KeepTypesFilterDefinition(
			builder._types.ToList(),
			builder._mode
		));
		return builder._parent;
	}
}

/// <summary>Builder for multiplexer token filters.</summary>
public sealed class MultiplexerFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private readonly List<string> _filters = [];
	private bool _preserveOriginal = true;

	internal MultiplexerFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the filters to multiplex through.</summary>
	public MultiplexerFilterBuilder Filters(params string[] filters)
	{
		_filters.AddRange(filters);
		return this;
	}

	/// <summary>Sets whether to preserve the original token.</summary>
	public MultiplexerFilterBuilder PreserveOriginal(bool preserve)
	{
		_preserveOriginal = preserve;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(MultiplexerFilterBuilder builder)
	{
		builder._parent.SetDefinition(new MultiplexerFilterDefinition(
			builder._filters.ToList(),
			builder._preserveOriginal
		));
		return builder._parent;
	}
}

/// <summary>Builder for condition token filters.</summary>
public sealed class ConditionFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private readonly List<string> _filter = [];
	private string _script = "";

	internal ConditionFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the filters to apply conditionally.</summary>
	public ConditionFilterBuilder Filter(params string[] filters)
	{
		_filter.AddRange(filters);
		return this;
	}

	/// <summary>Sets the condition script.</summary>
	public ConditionFilterBuilder Script(string script)
	{
		_script = script;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(ConditionFilterBuilder builder)
	{
		builder._parent.SetDefinition(new ConditionFilterDefinition(
			builder._filter.ToList(),
			builder._script
		));
		return builder._parent;
	}
}

/// <summary>Builder for hunspell token filters.</summary>
public sealed class HunspellFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private string _locale = "en_US";
	private bool _dedup = true;
	private bool _longestOnly;

	internal HunspellFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the locale.</summary>
	public HunspellFilterBuilder Locale(string locale)
	{
		_locale = locale;
		return this;
	}

	/// <summary>Sets whether to deduplicate.</summary>
	public HunspellFilterBuilder Dedup(bool dedup)
	{
		_dedup = dedup;
		return this;
	}

	/// <summary>Sets whether to only output the longest stem.</summary>
	public HunspellFilterBuilder LongestOnly(bool longestOnly = true)
	{
		_longestOnly = longestOnly;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(HunspellFilterBuilder builder)
	{
		builder._parent.SetDefinition(new HunspellFilterDefinition(
			builder._locale,
			builder._dedup,
			builder._longestOnly
		));
		return builder._parent;
	}
}

/// <summary>Builder for common_grams token filters.</summary>
public sealed class CommonGramsFilterBuilder
{
	private readonly TokenFilterBuilder _parent;
	private List<string>? _commonWords;
	private string? _commonWordsPath;
	private bool _ignoreCase;
	private string? _queryMode;

	internal CommonGramsFilterBuilder(TokenFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the common words.</summary>
	public CommonGramsFilterBuilder CommonWords(params string[] words)
	{
		_commonWords = [.. words];
		return this;
	}

	/// <summary>Sets the path to the common words file.</summary>
	public CommonGramsFilterBuilder CommonWordsPath(string path)
	{
		_commonWordsPath = path;
		return this;
	}

	/// <summary>Sets whether to ignore case.</summary>
	public CommonGramsFilterBuilder IgnoreCase(bool ignoreCase = true)
	{
		_ignoreCase = ignoreCase;
		return this;
	}

	/// <summary>Sets the query mode.</summary>
	public CommonGramsFilterBuilder QueryMode(string mode)
	{
		_queryMode = mode;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenFilterBuilder(CommonGramsFilterBuilder builder)
	{
		builder._parent.SetDefinition(new CommonGramsFilterDefinition(
			builder._commonWords,
			builder._commonWordsPath,
			builder._ignoreCase,
			builder._queryMode
		));
		return builder._parent;
	}
}
