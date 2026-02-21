// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Nodes;

namespace Elastic.Mapping.Analysis.Definitions;

/// <summary>Marker interface for token filter definitions.</summary>
public interface ITokenFilterDefinition
{
	/// <summary>Converts the definition to a JSON object for Elasticsearch.</summary>
	JsonObject ToJson();
}

/// <summary>An edge_ngram token filter definition.</summary>
public sealed record EdgeNGramFilterDefinition(
	int MinGram = 1,
	int MaxGram = 2,
	bool PreserveOriginal = false,
	string Side = "front"
) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = "edge_ngram",
			["min_gram"] = MinGram,
			["max_gram"] = MaxGram
		};

		if (PreserveOriginal)
			obj["preserve_original"] = true;

		if (Side != "front")
			obj["side"] = Side;

		return obj;
	}
}

/// <summary>A stop token filter definition.</summary>
public sealed record StopFilterDefinition(
	string? Stopwords = null,
	IReadOnlyList<string>? StopwordsList = null,
	bool IgnoreCase = false,
	bool RemoveTrailing = true
) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "stop" };

		if (Stopwords != null)
			obj["stopwords"] = Stopwords;
		else if (StopwordsList is { Count: > 0 })
			obj["stopwords"] = new JsonArray(StopwordsList.Select(s => JsonValue.Create(s)).ToArray());

		if (IgnoreCase)
			obj["ignore_case"] = true;

		if (!RemoveTrailing)
			obj["remove_trailing"] = false;

		return obj;
	}
}

/// <summary>A stemmer token filter definition.</summary>
public sealed record StemmerFilterDefinition(string Language) : ITokenFilterDefinition
{
	public JsonObject ToJson() => new()
	{
		["type"] = "stemmer",
		["language"] = Language
	};
}

/// <summary>A shingle token filter definition.</summary>
public sealed record ShingleFilterDefinition(
	int MinShingleSize = 2,
	int MaxShingleSize = 2,
	bool OutputUnigrams = true,
	bool OutputUnigramsIfNoShingles = false,
	string TokenSeparator = " ",
	string? FillerToken = null
) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = "shingle",
			["min_shingle_size"] = MinShingleSize,
			["max_shingle_size"] = MaxShingleSize
		};

		if (!OutputUnigrams)
			obj["output_unigrams"] = false;

		if (OutputUnigramsIfNoShingles)
			obj["output_unigrams_if_no_shingles"] = true;

		if (TokenSeparator != " ")
			obj["token_separator"] = TokenSeparator;

		if (FillerToken != null)
			obj["filler_token"] = FillerToken;

		return obj;
	}
}

/// <summary>A word_delimiter_graph token filter definition.</summary>
public sealed record WordDelimiterGraphFilterDefinition(
	bool PreserveOriginal = false,
	bool SplitOnCaseChange = true,
	bool SplitOnNumerics = true,
	bool GenerateWordParts = true,
	bool GenerateNumberParts = true,
	bool CatenateWords = false,
	bool CatenateNumbers = false,
	bool CatenateAll = false,
	bool StemEnglishPossessive = true,
	IReadOnlyList<string>? ProtectedWords = null,
	IReadOnlyList<string>? TypeTable = null
) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "word_delimiter_graph" };

		if (PreserveOriginal)
			obj["preserve_original"] = true;

		if (!SplitOnCaseChange)
			obj["split_on_case_change"] = false;

		if (!SplitOnNumerics)
			obj["split_on_numerics"] = false;

		if (!GenerateWordParts)
			obj["generate_word_parts"] = false;

		if (!GenerateNumberParts)
			obj["generate_number_parts"] = false;

		if (CatenateWords)
			obj["catenate_words"] = true;

		if (CatenateNumbers)
			obj["catenate_numbers"] = true;

		if (CatenateAll)
			obj["catenate_all"] = true;

		if (!StemEnglishPossessive)
			obj["stem_english_possessive"] = false;

		if (ProtectedWords is { Count: > 0 })
			obj["protected_words"] = new JsonArray(ProtectedWords.Select(w => JsonValue.Create(w)).ToArray());

		if (TypeTable is { Count: > 0 })
			obj["type_table"] = new JsonArray(TypeTable.Select(t => JsonValue.Create(t)).ToArray());

		return obj;
	}
}

/// <summary>A pattern_capture token filter definition.</summary>
public sealed record PatternCaptureFilterDefinition(
	IReadOnlyList<string> Patterns,
	bool PreserveOriginal = true
) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = "pattern_capture",
			["patterns"] = new JsonArray(Patterns.Select(p => JsonValue.Create(p)).ToArray())
		};

		if (!PreserveOriginal)
			obj["preserve_original"] = false;

		return obj;
	}
}

/// <summary>A pattern_replace token filter definition.</summary>
public sealed record PatternReplaceFilterDefinition(
	string Pattern,
	string? Replacement = null,
	string? Flags = null,
	bool All = true
) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = "pattern_replace",
			["pattern"] = Pattern
		};

		if (Replacement != null)
			obj["replacement"] = Replacement;

		if (Flags != null)
			obj["flags"] = Flags;

		if (!All)
			obj["all"] = false;

		return obj;
	}
}

/// <summary>A synonym token filter definition.</summary>
public sealed record SynonymFilterDefinition(
	IReadOnlyList<string>? Synonyms = null,
	string? SynonymsPath = null,
	string? SynonymsSet = null,
	bool Expand = true,
	bool Lenient = false,
	bool? Updateable = null
) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "synonym" };

		if (Synonyms is { Count: > 0 })
			obj["synonyms"] = new JsonArray(Synonyms.Select(s => JsonValue.Create(s)).ToArray());

		if (SynonymsPath != null)
			obj["synonyms_path"] = SynonymsPath;

		if (SynonymsSet != null)
			obj["synonyms_set"] = SynonymsSet;

		if (!Expand)
			obj["expand"] = false;

		if (Lenient)
			obj["lenient"] = true;

		if (Updateable == true)
			obj["updateable"] = true;

		return obj;
	}
}

/// <summary>A synonym_graph token filter definition.</summary>
public sealed record SynonymGraphFilterDefinition(
	IReadOnlyList<string>? Synonyms = null,
	string? SynonymsPath = null,
	string? SynonymsSet = null,
	bool Expand = true,
	bool Lenient = false,
	bool? Updateable = null
) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "synonym_graph" };

		if (Synonyms is { Count: > 0 })
			obj["synonyms"] = new JsonArray(Synonyms.Select(s => JsonValue.Create(s)).ToArray());

		if (SynonymsPath != null)
			obj["synonyms_path"] = SynonymsPath;

		if (SynonymsSet != null)
			obj["synonyms_set"] = SynonymsSet;

		if (!Expand)
			obj["expand"] = false;

		if (Lenient)
			obj["lenient"] = true;

		if (Updateable == true)
			obj["updateable"] = true;

		return obj;
	}
}

/// <summary>A lowercase token filter definition.</summary>
public sealed record LowercaseFilterDefinition(string? Language = null) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "lowercase" };

		if (Language != null)
			obj["language"] = Language;

		return obj;
	}
}

/// <summary>An uppercase token filter definition.</summary>
public sealed record UppercaseFilterDefinition : ITokenFilterDefinition
{
	public JsonObject ToJson() => new() { ["type"] = "uppercase" };
}

/// <summary>An asciifolding token filter definition.</summary>
public sealed record AsciiFoldingFilterDefinition(bool PreserveOriginal = false) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "asciifolding" };

		if (PreserveOriginal)
			obj["preserve_original"] = true;

		return obj;
	}
}

/// <summary>A trim token filter definition.</summary>
public sealed record TrimFilterDefinition : ITokenFilterDefinition
{
	public JsonObject ToJson() => new() { ["type"] = "trim" };
}

/// <summary>A truncate token filter definition.</summary>
public sealed record TruncateFilterDefinition(int Length = 10) : ITokenFilterDefinition
{
	public JsonObject ToJson() => new()
	{
		["type"] = "truncate",
		["length"] = Length
	};
}

/// <summary>A unique token filter definition.</summary>
public sealed record UniqueFilterDefinition(bool OnlyOnSamePosition = false) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "unique" };

		if (OnlyOnSamePosition)
			obj["only_on_same_position"] = true;

		return obj;
	}
}

/// <summary>A length token filter definition.</summary>
public sealed record LengthFilterDefinition(int Min = 0, int Max = int.MaxValue) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "length" };

		if (Min > 0)
			obj["min"] = Min;

		if (Max < int.MaxValue)
			obj["max"] = Max;

		return obj;
	}
}

/// <summary>An ngram token filter definition.</summary>
public sealed record NGramFilterDefinition(
	int MinGram = 1,
	int MaxGram = 2,
	bool PreserveOriginal = false
) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = "ngram",
			["min_gram"] = MinGram,
			["max_gram"] = MaxGram
		};

		if (PreserveOriginal)
			obj["preserve_original"] = true;

		return obj;
	}
}

/// <summary>A reverse token filter definition.</summary>
public sealed record ReverseFilterDefinition : ITokenFilterDefinition
{
	public JsonObject ToJson() => new() { ["type"] = "reverse" };
}

/// <summary>An elision token filter definition.</summary>
public sealed record ElisionFilterDefinition(
	IReadOnlyList<string>? Articles = null,
	string? ArticlesPath = null,
	bool ArticlesCase = false
) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "elision" };

		if (Articles is { Count: > 0 })
			obj["articles"] = new JsonArray(Articles.Select(a => JsonValue.Create(a)).ToArray());

		if (ArticlesPath != null)
			obj["articles_path"] = ArticlesPath;

		if (ArticlesCase)
			obj["articles_case"] = true;

		return obj;
	}
}

/// <summary>A snowball token filter definition.</summary>
public sealed record SnowballFilterDefinition(string Language) : ITokenFilterDefinition
{
	public JsonObject ToJson() => new()
	{
		["type"] = "snowball",
		["language"] = Language
	};
}

/// <summary>A kstem token filter definition.</summary>
public sealed record KStemFilterDefinition : ITokenFilterDefinition
{
	public JsonObject ToJson() => new() { ["type"] = "kstem" };
}

/// <summary>A porter_stem token filter definition.</summary>
public sealed record PorterStemFilterDefinition : ITokenFilterDefinition
{
	public JsonObject ToJson() => new() { ["type"] = "porter_stem" };
}

/// <summary>A keep_words token filter definition.</summary>
public sealed record KeepWordsFilterDefinition(
	IReadOnlyList<string>? KeepWords = null,
	string? KeepWordsPath = null,
	bool KeepWordsCase = false
) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "keep" };

		if (KeepWords is { Count: > 0 })
			obj["keep_words"] = new JsonArray(KeepWords.Select(w => JsonValue.Create(w)).ToArray());

		if (KeepWordsPath != null)
			obj["keep_words_path"] = KeepWordsPath;

		if (KeepWordsCase)
			obj["keep_words_case"] = true;

		return obj;
	}
}

/// <summary>A keep_types token filter definition.</summary>
public sealed record KeepTypesFilterDefinition(
	IReadOnlyList<string> Types,
	string Mode = "include"
) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = "keep_types",
			["types"] = new JsonArray(Types.Select(t => JsonValue.Create(t)).ToArray())
		};

		if (Mode != "include")
			obj["mode"] = Mode;

		return obj;
	}
}

/// <summary>A multiplexer token filter definition.</summary>
public sealed record MultiplexerFilterDefinition(
	IReadOnlyList<string> Filters,
	bool PreserveOriginal = true
) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = "multiplexer",
			["filters"] = new JsonArray(Filters.Select(f => JsonValue.Create(f)).ToArray())
		};

		if (!PreserveOriginal)
			obj["preserve_original"] = false;

		return obj;
	}
}

/// <summary>A condition token filter definition.</summary>
public sealed record ConditionFilterDefinition(
	IReadOnlyList<string> Filter,
	string Script
) : ITokenFilterDefinition
{
	public JsonObject ToJson() => new()
	{
		["type"] = "condition",
		["filter"] = new JsonArray(Filter.Select(f => JsonValue.Create(f)).ToArray()),
		["script"] = new JsonObject { ["source"] = Script }
	};
}

/// <summary>A predicate_token_filter token filter definition.</summary>
public sealed record PredicateTokenFilterDefinition(string Script) : ITokenFilterDefinition
{
	public JsonObject ToJson() => new()
	{
		["type"] = "predicate_token_filter",
		["script"] = new JsonObject { ["source"] = Script }
	};
}

/// <summary>A hunspell token filter definition.</summary>
public sealed record HunspellFilterDefinition(
	string Locale,
	bool Dedup = true,
	bool LongestOnly = false
) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = "hunspell",
			["locale"] = Locale
		};

		if (!Dedup)
			obj["dedup"] = false;

		if (LongestOnly)
			obj["longest_only"] = true;

		return obj;
	}
}

/// <summary>A common_grams token filter definition.</summary>
public sealed record CommonGramsFilterDefinition(
	IReadOnlyList<string>? CommonWords = null,
	string? CommonWordsPath = null,
	bool IgnoreCase = false,
	string? QueryMode = null
) : ITokenFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "common_grams" };

		if (CommonWords is { Count: > 0 })
			obj["common_words"] = new JsonArray(CommonWords.Select(w => JsonValue.Create(w)).ToArray());

		if (CommonWordsPath != null)
			obj["common_words_path"] = CommonWordsPath;

		if (IgnoreCase)
			obj["ignore_case"] = true;

		if (QueryMode != null)
			obj["query_mode"] = QueryMode;

		return obj;
	}
}
