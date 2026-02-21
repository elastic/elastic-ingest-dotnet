// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Nodes;

namespace Elastic.Mapping.Analysis.Definitions;

/// <summary>Marker interface for tokenizer definitions.</summary>
public interface ITokenizerDefinition
{
	/// <summary>Converts the definition to a JSON object for Elasticsearch.</summary>
	JsonObject ToJson();
}

/// <summary>A pattern tokenizer definition.</summary>
public sealed record PatternTokenizerDefinition(
	string Pattern,
	string? Flags = null,
	int Group = 0
) : ITokenizerDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = "pattern",
			["pattern"] = Pattern
		};

		if (Flags != null)
			obj["flags"] = Flags;

		if (Group != 0)
			obj["group"] = Group;

		return obj;
	}
}

/// <summary>A char_group tokenizer definition.</summary>
public sealed record CharGroupTokenizerDefinition(
	IReadOnlyList<string> TokenizeOnChars,
	int? MaxTokenLength = null
) : ITokenizerDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = "char_group",
			["tokenize_on_chars"] = new JsonArray(TokenizeOnChars.Select(c => JsonValue.Create(c)).ToArray())
		};

		if (MaxTokenLength.HasValue)
			obj["max_token_length"] = MaxTokenLength.Value;

		return obj;
	}
}

/// <summary>An edge_ngram tokenizer definition.</summary>
public sealed record EdgeNGramTokenizerDefinition(
	int MinGram = 1,
	int MaxGram = 2,
	IReadOnlyList<string>? TokenChars = null
) : ITokenizerDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = "edge_ngram",
			["min_gram"] = MinGram,
			["max_gram"] = MaxGram
		};

		if (TokenChars is { Count: > 0 })
			obj["token_chars"] = new JsonArray(TokenChars.Select(tc => JsonValue.Create(tc)).ToArray());

		return obj;
	}
}

/// <summary>An ngram tokenizer definition.</summary>
public sealed record NGramTokenizerDefinition(
	int MinGram = 1,
	int MaxGram = 2,
	IReadOnlyList<string>? TokenChars = null
) : ITokenizerDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = "ngram",
			["min_gram"] = MinGram,
			["max_gram"] = MaxGram
		};

		if (TokenChars is { Count: > 0 })
			obj["token_chars"] = new JsonArray(TokenChars.Select(tc => JsonValue.Create(tc)).ToArray());

		return obj;
	}
}

/// <summary>A path_hierarchy tokenizer definition.</summary>
public sealed record PathHierarchyTokenizerDefinition(
	char Delimiter = '/',
	char? Replacement = null,
	int Skip = 0,
	bool Reverse = false
) : ITokenizerDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = "path_hierarchy",
			["delimiter"] = Delimiter.ToString()
		};

		if (Replacement.HasValue)
			obj["replacement"] = Replacement.Value.ToString();

		if (Skip != 0)
			obj["skip"] = Skip;

		if (Reverse)
			obj["reverse"] = true;

		return obj;
	}
}

/// <summary>A UAX URL email tokenizer definition.</summary>
public sealed record UaxUrlEmailTokenizerDefinition(
	int? MaxTokenLength = null
) : ITokenizerDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "uax_url_email" };

		if (MaxTokenLength.HasValue)
			obj["max_token_length"] = MaxTokenLength.Value;

		return obj;
	}
}

/// <summary>A keyword tokenizer definition.</summary>
public sealed record KeywordTokenizerDefinition(int? BufferSize = null) : ITokenizerDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "keyword" };

		if (BufferSize.HasValue)
			obj["buffer_size"] = BufferSize.Value;

		return obj;
	}
}

/// <summary>A standard tokenizer definition.</summary>
public sealed record StandardTokenizerDefinition(int? MaxTokenLength = null) : ITokenizerDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "standard" };

		if (MaxTokenLength.HasValue)
			obj["max_token_length"] = MaxTokenLength.Value;

		return obj;
	}
}

/// <summary>A whitespace tokenizer definition.</summary>
public sealed record WhitespaceTokenizerDefinition(int? MaxTokenLength = null) : ITokenizerDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "whitespace" };

		if (MaxTokenLength.HasValue)
			obj["max_token_length"] = MaxTokenLength.Value;

		return obj;
	}
}

/// <summary>A simple pattern tokenizer definition.</summary>
public sealed record SimplePatternTokenizerDefinition(string Pattern) : ITokenizerDefinition
{
	public JsonObject ToJson() => new()
	{
		["type"] = "simple_pattern",
		["pattern"] = Pattern
	};
}

/// <summary>A simple pattern split tokenizer definition.</summary>
public sealed record SimplePatternSplitTokenizerDefinition(string Pattern) : ITokenizerDefinition
{
	public JsonObject ToJson() => new()
	{
		["type"] = "simple_pattern_split",
		["pattern"] = Pattern
	};
}
