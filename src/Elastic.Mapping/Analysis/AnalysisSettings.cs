// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Nodes;
using Elastic.Mapping.Analysis.Definitions;

namespace Elastic.Mapping.Analysis;

/// <summary>
/// Immutable analysis settings configuration.
/// Contains analyzers, tokenizers, token filters, normalizers, and char filters.
/// </summary>
public sealed class AnalysisSettings
{
	/// <summary>The configured analyzers.</summary>
	public IReadOnlyDictionary<string, IAnalyzerDefinition> Analyzers { get; }

	/// <summary>The configured tokenizers.</summary>
	public IReadOnlyDictionary<string, ITokenizerDefinition> Tokenizers { get; }

	/// <summary>The configured token filters.</summary>
	public IReadOnlyDictionary<string, ITokenFilterDefinition> TokenFilters { get; }

	/// <summary>The configured normalizers.</summary>
	public IReadOnlyDictionary<string, INormalizerDefinition> Normalizers { get; }

	/// <summary>The configured char filters.</summary>
	public IReadOnlyDictionary<string, ICharFilterDefinition> CharFilters { get; }

	internal AnalysisSettings(
		IReadOnlyDictionary<string, IAnalyzerDefinition> analyzers,
		IReadOnlyDictionary<string, ITokenizerDefinition> tokenizers,
		IReadOnlyDictionary<string, ITokenFilterDefinition> tokenFilters,
		IReadOnlyDictionary<string, INormalizerDefinition> normalizers,
		IReadOnlyDictionary<string, ICharFilterDefinition> charFilters)
	{
		Analyzers = analyzers;
		Tokenizers = tokenizers;
		TokenFilters = tokenFilters;
		Normalizers = normalizers;
		CharFilters = charFilters;
	}

	/// <summary>Returns true if any analysis components are configured.</summary>
	public bool HasConfiguration =>
		Analyzers.Count > 0 ||
		Tokenizers.Count > 0 ||
		TokenFilters.Count > 0 ||
		Normalizers.Count > 0 ||
		CharFilters.Count > 0;

	/// <summary>
	/// Converts the analysis settings to a JSON object suitable for Elasticsearch.
	/// </summary>
	public JsonObject ToJson()
	{
		var analysis = new JsonObject();

		if (Analyzers.Count > 0)
		{
			var obj = new JsonObject();
			foreach (var kvp in Analyzers)
				obj[kvp.Key] = kvp.Value.ToJson();
			analysis["analyzer"] = obj;
		}

		if (Tokenizers.Count > 0)
		{
			var obj = new JsonObject();
			foreach (var kvp in Tokenizers)
				obj[kvp.Key] = kvp.Value.ToJson();
			analysis["tokenizer"] = obj;
		}

		if (TokenFilters.Count > 0)
		{
			var obj = new JsonObject();
			foreach (var kvp in TokenFilters)
				obj[kvp.Key] = kvp.Value.ToJson();
			analysis["filter"] = obj;
		}

		if (Normalizers.Count > 0)
		{
			var obj = new JsonObject();
			foreach (var kvp in Normalizers)
				obj[kvp.Key] = kvp.Value.ToJson();
			analysis["normalizer"] = obj;
		}

		if (CharFilters.Count > 0)
		{
			var obj = new JsonObject();
			foreach (var kvp in CharFilters)
				obj[kvp.Key] = kvp.Value.ToJson();
			analysis["char_filter"] = obj;
		}

		return analysis;
	}

	/// <summary>
	/// Converts the analysis settings to a JSON string.
	/// </summary>
	public string ToJsonString(bool indented = false) =>
		ToJson().ToJsonString(new JsonSerializerOptions { WriteIndented = indented });

	/// <summary>
	/// Merges this analysis settings into an existing settings JSON string.
	/// </summary>
	public string MergeIntoSettings(string settingsJson)
	{
		if (!HasConfiguration)
			return settingsJson;

		var node = JsonNode.Parse(settingsJson) ?? new JsonObject();
		var settings = node["settings"]?.AsObject();

		if (settings == null)
		{
			settings = [];
			node.AsObject()["settings"] = settings;
		}

		var existingAnalysis = settings["analysis"]?.AsObject();
		if (existingAnalysis == null)
		{
			existingAnalysis = [];
			settings["analysis"] = existingAnalysis;
		}

		var analysisJson = ToJson();
		foreach (var kvp in analysisJson)
		{
			if (kvp.Value is not JsonObject sectionToAdd)
				continue;

			var existingSection = existingAnalysis[kvp.Key]?.AsObject();
			if (existingSection == null)
			{
				existingSection = [];
				existingAnalysis[kvp.Key] = existingSection;
			}

			foreach (var entry in sectionToAdd)
				existingSection[entry.Key] = entry.Value?.DeepClone();
		}

		return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
	}

	/// <summary>
	/// Creates an empty AnalysisSettings instance.
	/// </summary>
	public static AnalysisSettings Empty { get; } = new(
		new Dictionary<string, IAnalyzerDefinition>(),
		new Dictionary<string, ITokenizerDefinition>(),
		new Dictionary<string, ITokenFilterDefinition>(),
		new Dictionary<string, INormalizerDefinition>(),
		new Dictionary<string, ICharFilterDefinition>()
	);
}
