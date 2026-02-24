// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elastic.AgentBuilder.Conversations;

/// <summary>
/// Synchronous response from the converse API.
/// </summary>
public record ConverseResponse
{
	[JsonPropertyName("conversation_id")]
	public required string ConversationId { get; init; }

	[JsonPropertyName("round_id")]
	public string? RoundId { get; init; }

	[JsonPropertyName("status")]
	public string? Status { get; init; }

	[JsonPropertyName("steps")]
	public IReadOnlyList<ConverseStep>? Steps { get; init; }

	[JsonPropertyName("model_usage")]
	public ModelUsage? ModelUsage { get; init; }

	[JsonPropertyName("response")]
	public ConverseMessage? Response { get; init; }
}

/// <summary>
/// A step in the agent's processing of a conversation round.
/// </summary>
public record ConverseStep
{
	[JsonPropertyName("type")]
	public required string Type { get; init; }

	[JsonPropertyName("reasoning")]
	public string? Reasoning { get; init; }

	[JsonPropertyName("tool_id")]
	public string? ToolId { get; init; }

	[JsonPropertyName("params")]
	public JsonElement? Params { get; init; }

	[JsonPropertyName("results")]
	public IReadOnlyList<JsonElement>? Results { get; init; }
}

/// <summary>
/// The agent's response message.
/// </summary>
public record ConverseMessage
{
	[JsonPropertyName("message")]
	public required string Message { get; init; }
}

/// <summary>
/// Token usage information for a conversation round.
/// </summary>
public record ModelUsage
{
	[JsonPropertyName("input_tokens")]
	public int InputTokens { get; init; }

	[JsonPropertyName("output_tokens")]
	public int OutputTokens { get; init; }
}
