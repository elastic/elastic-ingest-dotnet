// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Elastic.AgentBuilder.Agents;

/// <summary>
/// Represents an agent as returned by the Agent Builder API.
/// </summary>
public record AgentBuilderAgent
{
	[JsonPropertyName("id")]
	public required string Id { get; init; }

	[JsonPropertyName("type")]
	public string? Type { get; init; }

	[JsonPropertyName("name")]
	public required string Name { get; init; }

	[JsonPropertyName("description")]
	public string? Description { get; init; }

	[JsonPropertyName("labels")]
	public IReadOnlyList<string>? Labels { get; init; }

	[JsonPropertyName("avatar_color")]
	public string? AvatarColor { get; init; }

	[JsonPropertyName("avatar_symbol")]
	public string? AvatarSymbol { get; init; }

	[JsonPropertyName("configuration")]
	public AgentConfiguration? Configuration { get; init; }

	[JsonPropertyName("readonly")]
	public bool Readonly { get; init; }
}

/// <summary>
/// Configuration settings for an agent.
/// </summary>
public record AgentConfiguration
{
	[JsonPropertyName("instructions")]
	public string? Instructions { get; init; }

	[JsonPropertyName("tools")]
	public IReadOnlyList<AgentToolGroup>? Tools { get; init; }
}

/// <summary>
/// A group of tool IDs assigned to an agent.
/// </summary>
public record AgentToolGroup
{
	[JsonPropertyName("tool_ids")]
	public required IReadOnlyList<string> ToolIds { get; init; }
}

/// <summary>
/// Response wrapper for listing agents.
/// </summary>
public record ListAgentsResponse
{
	[JsonPropertyName("results")]
	public required IReadOnlyList<AgentBuilderAgent> Results { get; init; }
}
