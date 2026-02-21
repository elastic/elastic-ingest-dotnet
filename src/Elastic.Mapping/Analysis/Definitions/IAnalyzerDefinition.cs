// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Nodes;

namespace Elastic.Mapping.Analysis.Definitions;

/// <summary>Marker interface for analyzer definitions.</summary>
public interface IAnalyzerDefinition
{
	/// <summary>Converts the definition to a JSON object for Elasticsearch.</summary>
	JsonObject ToJson();
}

/// <summary>A custom analyzer definition with configurable tokenizer and filters.</summary>
public sealed record CustomAnalyzerDefinition(
	string Tokenizer,
	IReadOnlyList<string> Filters,
	IReadOnlyList<string> CharFilters
) : IAnalyzerDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = "custom",
			["tokenizer"] = Tokenizer
		};

		if (Filters.Count > 0)
			obj["filter"] = new JsonArray(Filters.Select(f => JsonValue.Create(f)).ToArray());

		if (CharFilters.Count > 0)
			obj["char_filter"] = new JsonArray(CharFilters.Select(cf => JsonValue.Create(cf)).ToArray());

		return obj;
	}
}

/// <summary>A pattern analyzer definition.</summary>
public sealed record PatternAnalyzerDefinition(
	string Pattern,
	bool Lowercase = true,
	string? Stopwords = null
) : IAnalyzerDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = "pattern",
			["pattern"] = Pattern,
			["lowercase"] = Lowercase
		};

		if (Stopwords != null)
			obj["stopwords"] = Stopwords;

		return obj;
	}
}

/// <summary>A standard analyzer definition with optional stopwords.</summary>
public sealed record StandardAnalyzerDefinition(
	string? Stopwords = null,
	int? MaxTokenLength = null
) : IAnalyzerDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "standard" };

		if (Stopwords != null)
			obj["stopwords"] = Stopwords;

		if (MaxTokenLength.HasValue)
			obj["max_token_length"] = MaxTokenLength.Value;

		return obj;
	}
}

/// <summary>A simple analyzer definition.</summary>
public sealed record SimpleAnalyzerDefinition : IAnalyzerDefinition
{
	public JsonObject ToJson() => new() { ["type"] = "simple" };
}

/// <summary>A whitespace analyzer definition.</summary>
public sealed record WhitespaceAnalyzerDefinition : IAnalyzerDefinition
{
	public JsonObject ToJson() => new() { ["type"] = "whitespace" };
}

/// <summary>A keyword analyzer definition (no tokenization).</summary>
public sealed record KeywordAnalyzerDefinition : IAnalyzerDefinition
{
	public JsonObject ToJson() => new() { ["type"] = "keyword" };
}

/// <summary>A language-specific analyzer definition.</summary>
public sealed record LanguageAnalyzerDefinition(string Language) : IAnalyzerDefinition
{
	public JsonObject ToJson() => new() { ["type"] = Language };
}

/// <summary>A fingerprint analyzer definition.</summary>
public sealed record FingerprintAnalyzerDefinition(
	char? Separator = null,
	int? MaxOutputSize = null,
	string? Stopwords = null
) : IAnalyzerDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "fingerprint" };

		if (Separator.HasValue)
			obj["separator"] = Separator.Value.ToString();

		if (MaxOutputSize.HasValue)
			obj["max_output_size"] = MaxOutputSize.Value;

		if (Stopwords != null)
			obj["stopwords"] = Stopwords;

		return obj;
	}
}
