// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Elastic.AgentBuilder.Conversations;

namespace Elastic.AgentBuilder;

public partial class AgentBuilderClient
{
	/// <summary> Send a synchronous chat message to an agent. </summary>
	public Task<ConverseResponse> ConverseAsync(ConverseRequest request, CancellationToken ct = default) =>
		PostAsync("/converse", request, Ctx.ConverseRequest, Ctx.ConverseResponse, ct);

	/// <summary> Send a chat message and receive a stream of typed SSE events. </summary>
	public async IAsyncEnumerable<ConverseStreamEvent> ConverseStreamAsync(
		ConverseRequest request,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		using var response = await PostStreamAsync("/converse/async", request, Ctx.ConverseRequest, ct)
			.ConfigureAwait(false);

		await foreach (var item in SseParser.Create(response.Body).EnumerateAsync(ct).ConfigureAwait(false))
		{
			var evt = ParseStreamEvent(item.EventType, item.Data);
			if (evt != null)
				yield return evt;
		}
	}

	/// <summary> List all conversations. </summary>
	public Task<ListConversationsResponse> ListConversationsAsync(CancellationToken ct = default) =>
		GetAsync("/conversations", Ctx.ListConversationsResponse, ct);

	/// <summary> Get a conversation by its ID. </summary>
	public Task<Conversation> GetConversationAsync(string conversationId, CancellationToken ct = default) =>
		GetAsync($"/conversations/{conversationId}", Ctx.Conversation, ct);

	/// <summary> Delete a conversation by its ID. </summary>
	public Task DeleteConversationAsync(string conversationId, CancellationToken ct = default) =>
		DeleteAsync($"/conversations/{conversationId}", ct);

	private static ConverseStreamEvent? ParseStreamEvent(string eventType, string rawData)
	{
		using var doc = JsonDocument.Parse(rawData);
		if (!doc.RootElement.TryGetProperty("data", out var dataElement))
			return null;

		var json = dataElement.GetRawText();
		var ctx = AgentBuilderSerializationContext.Default;
		return eventType switch
		{
			"conversation_id_set" => JsonSerializer.Deserialize(json, ctx.ConversationIdSetEvent),
			"conversation_created" => JsonSerializer.Deserialize(json, ctx.ConversationCreatedEvent),
			"conversation_updated" => JsonSerializer.Deserialize(json, ctx.ConversationUpdatedEvent),
			"reasoning" => JsonSerializer.Deserialize(json, ctx.ReasoningEvent),
			"tool_call" => JsonSerializer.Deserialize(json, ctx.ToolCallEvent),
			"tool_progress" => JsonSerializer.Deserialize(json, ctx.ToolProgressEvent),
			"tool_result" => JsonSerializer.Deserialize(json, ctx.ToolResultEvent),
			"message_chunk" => JsonSerializer.Deserialize(json, ctx.MessageChunkEvent),
			"message_complete" => JsonSerializer.Deserialize(json, ctx.MessageCompleteEvent),
			"thinking_complete" => JsonSerializer.Deserialize(json, ctx.ThinkingCompleteEvent),
			"round_complete" => JsonSerializer.Deserialize(json, ctx.RoundCompleteEvent),
			_ => null
		};
	}
}
