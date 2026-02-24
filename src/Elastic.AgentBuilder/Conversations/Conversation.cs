// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elastic.AgentBuilder.Conversations;

/// <summary>
/// Represents a conversation as returned by the Agent Builder API.
/// </summary>
public record Conversation
{
	[JsonPropertyName("conversation_id")]
	public required string ConversationId { get; init; }

	[JsonPropertyName("title")]
	public string? Title { get; init; }

	[JsonPropertyName("agent_id")]
	public string? AgentId { get; init; }

	[JsonPropertyName("rounds")]
	public IReadOnlyList<JsonElement>? Rounds { get; init; }
}

/// <summary>
/// Response wrapper for listing conversations.
/// </summary>
public record ListConversationsResponse
{
	[JsonPropertyName("results")]
	public required IReadOnlyList<Conversation> Results { get; init; }
}
