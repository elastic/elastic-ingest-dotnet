// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping.Analysis;

/// <summary>
/// Built-in Elasticsearch analysis component names.
/// Use these constants when referencing standard analyzers, tokenizers, filters, etc.
/// </summary>
public static class BuiltInAnalysis
{
	/// <summary>Built-in Elasticsearch analyzers.</summary>
	public static class Analyzers
	{
		/// <summary>Standard analyzer - default for text fields.</summary>
		public const string Standard = "standard";

		/// <summary>Simple analyzer - divides text at non-letter characters.</summary>
		public const string Simple = "simple";

		/// <summary>Whitespace analyzer - divides text at whitespace.</summary>
		public const string Whitespace = "whitespace";

		/// <summary>Stop analyzer - simple analyzer with stop word removal.</summary>
		public const string Stop = "stop";

		/// <summary>Keyword analyzer - noop analyzer that returns the entire input as a single token.</summary>
		public const string Keyword = "keyword";

		/// <summary>Pattern analyzer - uses a regular expression to split text into terms.</summary>
		public const string Pattern = "pattern";

		/// <summary>Fingerprint analyzer - creates a fingerprint for duplicate detection.</summary>
		public const string Fingerprint = "fingerprint";

		/// <summary>Language-specific analyzers.</summary>
		public static class Language
		{
			public const string Arabic = "arabic";
			public const string Armenian = "armenian";
			public const string Basque = "basque";
			public const string Bengali = "bengali";
			public const string Brazilian = "brazilian";
			public const string Bulgarian = "bulgarian";
			public const string Catalan = "catalan";
			public const string Cjk = "cjk";
			public const string Czech = "czech";
			public const string Danish = "danish";
			public const string Dutch = "dutch";
			public const string English = "english";
			public const string Estonian = "estonian";
			public const string Finnish = "finnish";
			public const string French = "french";
			public const string Galician = "galician";
			public const string German = "german";
			public const string Greek = "greek";
			public const string Hindi = "hindi";
			public const string Hungarian = "hungarian";
			public const string Indonesian = "indonesian";
			public const string Irish = "irish";
			public const string Italian = "italian";
			public const string Latvian = "latvian";
			public const string Lithuanian = "lithuanian";
			public const string Norwegian = "norwegian";
			public const string Persian = "persian";
			public const string Portuguese = "portuguese";
			public const string Romanian = "romanian";
			public const string Russian = "russian";
			public const string Sorani = "sorani";
			public const string Spanish = "spanish";
			public const string Swedish = "swedish";
			public const string Thai = "thai";
			public const string Turkish = "turkish";
		}
	}

	/// <summary>Built-in Elasticsearch tokenizers.</summary>
	public static class Tokenizers
	{
		/// <summary>Standard tokenizer - grammar-based tokenizer.</summary>
		public const string Standard = "standard";

		/// <summary>Letter tokenizer - divides text at non-letters.</summary>
		public const string Letter = "letter";

		/// <summary>Lowercase tokenizer - like letter tokenizer but lowercases tokens.</summary>
		public const string Lowercase = "lowercase";

		/// <summary>Whitespace tokenizer - divides text at whitespace.</summary>
		public const string Whitespace = "whitespace";

		/// <summary>UAX URL Email tokenizer - like standard but recognizes URLs and emails as single tokens.</summary>
		public const string UaxUrlEmail = "uax_url_email";

		/// <summary>Classic tokenizer - grammar-based tokenizer for English.</summary>
		public const string Classic = "classic";

		/// <summary>Thai tokenizer - for Thai text.</summary>
		public const string Thai = "thai";

		/// <summary>NGram tokenizer - breaks text into ngrams.</summary>
		public const string NGram = "ngram";

		/// <summary>Edge NGram tokenizer - breaks text into edge ngrams.</summary>
		public const string EdgeNGram = "edge_ngram";

		/// <summary>Keyword tokenizer - noop tokenizer that returns the entire input as a single token.</summary>
		public const string Keyword = "keyword";

		/// <summary>Pattern tokenizer - uses a regular expression to split text.</summary>
		public const string Pattern = "pattern";

		/// <summary>Simple pattern tokenizer - uses a regular expression to capture matching text.</summary>
		public const string SimplePattern = "simple_pattern";

		/// <summary>Simple pattern split tokenizer - like simple_pattern but splits at matches.</summary>
		public const string SimplePatternSplit = "simple_pattern_split";

		/// <summary>Char group tokenizer - splits on defined character groups.</summary>
		public const string CharGroup = "char_group";

		/// <summary>Path hierarchy tokenizer - splits hierarchical paths.</summary>
		public const string PathHierarchy = "path_hierarchy";
	}

	/// <summary>Built-in Elasticsearch token filters.</summary>
	public static class TokenFilters
	{
		/// <summary>Lowercase filter - normalizes token text to lowercase.</summary>
		public const string Lowercase = "lowercase";

		/// <summary>Uppercase filter - normalizes token text to uppercase.</summary>
		public const string Uppercase = "uppercase";

		/// <summary>ASCII folding filter - converts Unicode characters to ASCII equivalent.</summary>
		public const string AsciiFolding = "asciifolding";

		/// <summary>Length filter - removes tokens shorter or longer than specified.</summary>
		public const string Length = "length";

		/// <summary>Truncate filter - truncates tokens to a specified length.</summary>
		public const string Truncate = "truncate";

		/// <summary>Trim filter - removes leading and trailing whitespace.</summary>
		public const string Trim = "trim";

		/// <summary>Unique filter - removes duplicate tokens in the same position.</summary>
		public const string Unique = "unique";

		/// <summary>Reverse filter - reverses each token.</summary>
		public const string Reverse = "reverse";

		/// <summary>Elision filter - removes elisions (e.g., l', d' in French).</summary>
		public const string Elision = "elision";

		/// <summary>Stop filter - removes stop words.</summary>
		public const string Stop = "stop";

		/// <summary>Keyword marker filter - protects words from being modified by stemmers.</summary>
		public const string KeywordMarker = "keyword_marker";

		/// <summary>Snowball filter - stemmer based on Snowball algorithms.</summary>
		public const string Snowball = "snowball";

		/// <summary>Stemmer filter - language-specific stemming.</summary>
		public const string Stemmer = "stemmer";

		/// <summary>Stemmer override filter - applies custom stemming rules.</summary>
		public const string StemmerOverride = "stemmer_override";

		/// <summary>Porter stem filter - Porter stemming algorithm for English.</summary>
		public const string PorterStem = "porter_stem";

		/// <summary>KStem filter - KStem algorithm for English.</summary>
		public const string KStem = "kstem";

		/// <summary>Synonym filter - handles synonyms.</summary>
		public const string Synonym = "synonym";

		/// <summary>Synonym graph filter - handles multi-word synonyms.</summary>
		public const string SynonymGraph = "synonym_graph";

		/// <summary>NGram filter - breaks tokens into ngrams.</summary>
		public const string NGram = "ngram";

		/// <summary>Edge NGram filter - breaks tokens into edge ngrams.</summary>
		public const string EdgeNGram = "edge_ngram";

		/// <summary>Shingle filter - creates shingles (word n-grams).</summary>
		public const string Shingle = "shingle";

		/// <summary>Word delimiter filter - splits words on boundaries.</summary>
		public const string WordDelimiter = "word_delimiter";

		/// <summary>Word delimiter graph filter - splits words on boundaries (graph-aware).</summary>
		public const string WordDelimiterGraph = "word_delimiter_graph";

		/// <summary>Flatten graph filter - flattens a token graph.</summary>
		public const string FlattenGraph = "flatten_graph";

		/// <summary>Multiplexer filter - emits multiple tokens at the same position.</summary>
		public const string Multiplexer = "multiplexer";

		/// <summary>Condition filter - conditionally applies filters.</summary>
		public const string Condition = "condition";

		/// <summary>Predicate token filter - removes tokens based on a predicate.</summary>
		public const string PredicateTokenFilter = "predicate_token_filter";

		/// <summary>Remove duplicates filter - removes duplicate tokens.</summary>
		public const string RemoveDuplicates = "remove_duplicates";

		/// <summary>Classic filter - optional post-processing for classic tokenizer.</summary>
		public const string Classic = "classic";

		/// <summary>Apostrophe filter - strips everything after an apostrophe.</summary>
		public const string Apostrophe = "apostrophe";

		/// <summary>Decimal digit filter - folds Unicode digits to 0-9.</summary>
		public const string DecimalDigit = "decimal_digit";

		/// <summary>Fingerprint filter - creates a single token fingerprint.</summary>
		public const string Fingerprint = "fingerprint";

		/// <summary>Min hash filter - produces min hash tokens for similarity matching.</summary>
		public const string MinHash = "min_hash";

		/// <summary>Common grams filter - generates bigrams for common terms.</summary>
		public const string CommonGrams = "common_grams";

		/// <summary>CJK bigram filter - forms bigrams of CJK terms.</summary>
		public const string CjkBigram = "cjk_bigram";

		/// <summary>CJK width filter - folds CJK width differences.</summary>
		public const string CjkWidth = "cjk_width";

		/// <summary>Delimited payload filter - splits tokens on delimiter, using second part as payload.</summary>
		public const string DelimitedPayload = "delimited_payload";

		/// <summary>Keep words filter - keeps only specified words.</summary>
		public const string KeepWords = "keep";

		/// <summary>Keep types filter - keeps only specified token types.</summary>
		public const string KeepTypes = "keep_types";

		/// <summary>Pattern capture filter - captures matching parts of a pattern.</summary>
		public const string PatternCapture = "pattern_capture";

		/// <summary>Pattern replace filter - replaces patterns in tokens.</summary>
		public const string PatternReplace = "pattern_replace";

		/// <summary>Phonetic filter - produces phonetic tokens.</summary>
		public const string Phonetic = "phonetic";

		/// <summary>ICU folding filter - Unicode case folding.</summary>
		public const string IcuFolding = "icu_folding";

		/// <summary>ICU normalizer filter - Unicode normalization.</summary>
		public const string IcuNormalizer = "icu_normalizer";

		/// <summary>ICU transform filter - applies ICU transforms.</summary>
		public const string IcuTransform = "icu_transform";

		/// <summary>ICU collation filter - sorts tokens for collation.</summary>
		public const string IcuCollation = "icu_collation";

		/// <summary>Hunspell filter - dictionary-based stemming.</summary>
		public const string Hunspell = "hunspell";
	}

	/// <summary>Built-in Elasticsearch character filters.</summary>
	public static class CharFilters
	{
		/// <summary>HTML strip filter - strips HTML tags.</summary>
		public const string HtmlStrip = "html_strip";

		/// <summary>Mapping filter - replaces characters based on a mapping.</summary>
		public const string Mapping = "mapping";

		/// <summary>Pattern replace filter - replaces patterns in character stream.</summary>
		public const string PatternReplace = "pattern_replace";

		/// <summary>ICU normalizer filter - Unicode normalization at char level.</summary>
		public const string IcuNormalizer = "icu_normalizer";
	}

	/// <summary>Built-in stop word lists that can be used with stop filters.</summary>
	public static class StopWords
	{
		/// <summary>No stop words.</summary>
		public const string None = "_none_";

		/// <summary>English stop words.</summary>
		public const string English = "_english_";

		/// <summary>Arabic stop words.</summary>
		public const string Arabic = "_arabic_";

		/// <summary>Armenian stop words.</summary>
		public const string Armenian = "_armenian_";

		/// <summary>Basque stop words.</summary>
		public const string Basque = "_basque_";

		/// <summary>Bengali stop words.</summary>
		public const string Bengali = "_bengali_";

		/// <summary>Brazilian stop words.</summary>
		public const string Brazilian = "_brazilian_";

		/// <summary>Bulgarian stop words.</summary>
		public const string Bulgarian = "_bulgarian_";

		/// <summary>Catalan stop words.</summary>
		public const string Catalan = "_catalan_";

		/// <summary>CJK stop words.</summary>
		public const string Cjk = "_cjk_";

		/// <summary>Czech stop words.</summary>
		public const string Czech = "_czech_";

		/// <summary>Danish stop words.</summary>
		public const string Danish = "_danish_";

		/// <summary>Dutch stop words.</summary>
		public const string Dutch = "_dutch_";

		/// <summary>Estonian stop words.</summary>
		public const string Estonian = "_estonian_";

		/// <summary>Finnish stop words.</summary>
		public const string Finnish = "_finnish_";

		/// <summary>French stop words.</summary>
		public const string French = "_french_";

		/// <summary>Galician stop words.</summary>
		public const string Galician = "_galician_";

		/// <summary>German stop words.</summary>
		public const string German = "_german_";

		/// <summary>Greek stop words.</summary>
		public const string Greek = "_greek_";

		/// <summary>Hindi stop words.</summary>
		public const string Hindi = "_hindi_";

		/// <summary>Hungarian stop words.</summary>
		public const string Hungarian = "_hungarian_";

		/// <summary>Indonesian stop words.</summary>
		public const string Indonesian = "_indonesian_";

		/// <summary>Irish stop words.</summary>
		public const string Irish = "_irish_";

		/// <summary>Italian stop words.</summary>
		public const string Italian = "_italian_";

		/// <summary>Latvian stop words.</summary>
		public const string Latvian = "_latvian_";

		/// <summary>Lithuanian stop words.</summary>
		public const string Lithuanian = "_lithuanian_";

		/// <summary>Norwegian stop words.</summary>
		public const string Norwegian = "_norwegian_";

		/// <summary>Persian stop words.</summary>
		public const string Persian = "_persian_";

		/// <summary>Portuguese stop words.</summary>
		public const string Portuguese = "_portuguese_";

		/// <summary>Romanian stop words.</summary>
		public const string Romanian = "_romanian_";

		/// <summary>Russian stop words.</summary>
		public const string Russian = "_russian_";

		/// <summary>Sorani stop words.</summary>
		public const string Sorani = "_sorani_";

		/// <summary>Spanish stop words.</summary>
		public const string Spanish = "_spanish_";

		/// <summary>Swedish stop words.</summary>
		public const string Swedish = "_swedish_";

		/// <summary>Thai stop words.</summary>
		public const string Thai = "_thai_";

		/// <summary>Turkish stop words.</summary>
		public const string Turkish = "_turkish_";
	}

	/// <summary>Stemmer language names for use with the stemmer filter.</summary>
	public static class StemmerLanguages
	{
		public const string Arabic = "arabic";
		public const string Armenian = "armenian";
		public const string Basque = "basque";
		public const string Bengali = "bengali";
		public const string LightBengali = "light_bengali";
		public const string Brazilian = "brazilian";
		public const string Bulgarian = "bulgarian";
		public const string Catalan = "catalan";
		public const string Czech = "czech";
		public const string Danish = "danish";
		public const string Dutch = "dutch";
		public const string DutchKp = "dutch_kp";
		public const string English = "english";
		public const string LightEnglish = "light_english";
		public const string Lovins = "lovins";
		public const string MinimalEnglish = "minimal_english";
		public const string Porter2 = "porter2";
		public const string PossessiveEnglish = "possessive_english";
		public const string Estonian = "estonian";
		public const string Finnish = "finnish";
		public const string LightFinnish = "light_finnish";
		public const string French = "french";
		public const string LightFrench = "light_french";
		public const string MinimalFrench = "minimal_french";
		public const string Galician = "galician";
		public const string MinimalGalician = "minimal_galician";
		public const string German = "german";
		public const string German2 = "german2";
		public const string LightGerman = "light_german";
		public const string MinimalGerman = "minimal_german";
		public const string Greek = "greek";
		public const string Hindi = "hindi";
		public const string Hungarian = "hungarian";
		public const string LightHungarian = "light_hungarian";
		public const string Indonesian = "indonesian";
		public const string Irish = "irish";
		public const string Italian = "italian";
		public const string LightItalian = "light_italian";
		public const string Sorani = "sorani";
		public const string Latvian = "latvian";
		public const string Lithuanian = "lithuanian";
		public const string Norwegian = "norwegian";
		public const string LightNorwegian = "light_norwegian";
		public const string MinimalNorwegian = "minimal_norwegian";
		public const string LightNynorsk = "light_nynorsk";
		public const string MinimalNynorsk = "minimal_nynorsk";
		public const string Portuguese = "portuguese";
		public const string LightPortuguese = "light_portuguese";
		public const string MinimalPortuguese = "minimal_portuguese";
		public const string PortugueseRslp = "portuguese_rslp";
		public const string Romanian = "romanian";
		public const string Russian = "russian";
		public const string LightRussian = "light_russian";
		public const string Spanish = "spanish";
		public const string LightSpanish = "light_spanish";
		public const string Swedish = "swedish";
		public const string LightSwedish = "light_swedish";
		public const string Turkish = "turkish";
	}
}
