// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Nodes;

namespace Elastic.Mapping.Mappings.Definitions;

/// <summary>A dynamic template definition.</summary>
public sealed record DynamicTemplateDefinition(
	string Name,
	string? Match = null,
	string? Unmatch = null,
	string? PathMatch = null,
	string? PathUnmatch = null,
	string? MatchMappingType = null,
	string? MatchPattern = null,
	IFieldDefinition? Mapping = null
)
{
	/// <summary>Converts the definition to a JSON object for Elasticsearch.</summary>
	public JsonObject ToJson()
	{
		var template = new JsonObject();

		if (Match != null)
			template["match"] = Match;

		if (Unmatch != null)
			template["unmatch"] = Unmatch;

		if (PathMatch != null)
			template["path_match"] = PathMatch;

		if (PathUnmatch != null)
			template["path_unmatch"] = PathUnmatch;

		if (MatchMappingType != null)
			template["match_mapping_type"] = MatchMappingType;

		if (MatchPattern != null)
			template["match_pattern"] = MatchPattern;

		if (Mapping != null)
			template["mapping"] = Mapping.ToJson();

		return new JsonObject { [Name] = template };
	}
}
