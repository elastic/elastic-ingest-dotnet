// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Nodes;

namespace Elastic.Mapping.Analysis.Definitions;

/// <summary>Marker interface for char filter definitions.</summary>
public interface ICharFilterDefinition
{
	/// <summary>Converts the definition to a JSON object for Elasticsearch.</summary>
	JsonObject ToJson();
}

/// <summary>A pattern_replace char filter definition.</summary>
public sealed record PatternReplaceCharFilterDefinition(
	string Pattern,
	string Replacement = "",
	string? Flags = null
) : ICharFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = "pattern_replace",
			["pattern"] = Pattern,
			["replacement"] = Replacement
		};

		if (Flags != null)
			obj["flags"] = Flags;

		return obj;
	}
}

/// <summary>A mapping char filter definition.</summary>
public sealed record MappingCharFilterDefinition(
	IReadOnlyList<string>? Mappings = null,
	string? MappingsPath = null
) : ICharFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "mapping" };

		if (Mappings is { Count: > 0 })
			obj["mappings"] = new JsonArray(Mappings.Select(m => JsonValue.Create(m)).ToArray());

		if (MappingsPath != null)
			obj["mappings_path"] = MappingsPath;

		return obj;
	}
}

/// <summary>An html_strip char filter definition.</summary>
public sealed record HtmlStripCharFilterDefinition(
	IReadOnlyList<string>? EscapedTags = null
) : ICharFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "html_strip" };

		if (EscapedTags is { Count: > 0 })
			obj["escaped_tags"] = new JsonArray(EscapedTags.Select(t => JsonValue.Create(t)).ToArray());

		return obj;
	}
}

/// <summary>A kuromoji_iteration_mark char filter definition.</summary>
public sealed record KuromojiIterationMarkCharFilterDefinition(
	bool NormalizeKanji = true,
	bool NormalizeKana = true
) : ICharFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "kuromoji_iteration_mark" };

		if (!NormalizeKanji)
			obj["normalize_kanji"] = false;

		if (!NormalizeKana)
			obj["normalize_kana"] = false;

		return obj;
	}
}

/// <summary>An icu_normalizer char filter definition.</summary>
public sealed record IcuNormalizerCharFilterDefinition(
	string Name = "nfkc_cf",
	string? Mode = null
) : ICharFilterDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = "icu_normalizer",
			["name"] = Name
		};

		if (Mode != null)
			obj["mode"] = Mode;

		return obj;
	}
}
