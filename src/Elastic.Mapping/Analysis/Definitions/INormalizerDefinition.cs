// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Nodes;

namespace Elastic.Mapping.Analysis.Definitions;

/// <summary>Marker interface for normalizer definitions.</summary>
public interface INormalizerDefinition
{
	/// <summary>Converts the definition to a JSON object for Elasticsearch.</summary>
	JsonObject ToJson();
}

/// <summary>A custom normalizer definition with configurable filters and char filters.</summary>
public sealed record CustomNormalizerDefinition(
	IReadOnlyList<string> Filters,
	IReadOnlyList<string> CharFilters
) : INormalizerDefinition
{
	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = "custom" };

		if (Filters.Count > 0)
			obj["filter"] = new JsonArray(Filters.Select(f => JsonValue.Create(f)).ToArray());

		if (CharFilters.Count > 0)
			obj["char_filter"] = new JsonArray(CharFilters.Select(cf => JsonValue.Create(cf)).ToArray());

		return obj;
	}
}

/// <summary>A lowercase normalizer definition.</summary>
public sealed record LowercaseNormalizerDefinition : INormalizerDefinition
{
	public JsonObject ToJson() => new() { ["type"] = "lowercase" };
}
