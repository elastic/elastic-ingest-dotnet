// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Nodes;

namespace Elastic.Mapping.Mappings.Definitions;

/// <summary>A runtime field definition.</summary>
public sealed record RuntimeFieldDefinition(
	string RuntimeType,
	string Script
)
{
	/// <summary>Converts the definition to a JSON object for Elasticsearch.</summary>
	public JsonObject ToJson() => new()
	{
		["type"] = RuntimeType,
		["script"] = new JsonObject
		{
			["source"] = Script
		}
	};
}
