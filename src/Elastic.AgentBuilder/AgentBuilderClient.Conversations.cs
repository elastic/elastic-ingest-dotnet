// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
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

		var parser = SseParser.Create(response.Body, ParseSseEvent);
		await foreach (var item in parser.EnumerateAsync(ct).ConfigureAwait(false))
		{
			if (item.Data is { } evt)
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

	/// <summary>
	/// Parses the raw UTF-8 bytes of an SSE <c>data:</c> line directly into a typed
	/// <see cref="ConverseStreamEvent"/>. Kibana wraps every event payload in a
	/// <c>{"data": {…}}</c> envelope, so we use a <see cref="Utf8JsonReader"/> to
	/// navigate to the inner value and deserialize from there — single pass, zero
	/// intermediate string allocations, fully AOT-safe.
	/// </summary>
	private static ConverseStreamEvent? ParseSseEvent(string eventType, ReadOnlySpan<byte> data)
	{
		var reader = new Utf8JsonReader(data);

		if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
			return null;
		if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName)
			return null;
		if (!reader.ValueTextEquals("data"u8))
			return null;

		reader.Read();

		var ctx = AgentBuilderSerializationContext.Default;
		return eventType switch
		{
			"conversation_id_set" => JsonSerializer.Deserialize(ref reader, ctx.ConversationIdSetEvent),
			"conversation_created" => JsonSerializer.Deserialize(ref reader, ctx.ConversationCreatedEvent),
			"conversation_updated" => JsonSerializer.Deserialize(ref reader, ctx.ConversationUpdatedEvent),
			"reasoning" => JsonSerializer.Deserialize(ref reader, ctx.ReasoningEvent),
			"tool_call" => JsonSerializer.Deserialize(ref reader, ctx.ToolCallEvent),
			"tool_progress" => JsonSerializer.Deserialize(ref reader, ctx.ToolProgressEvent),
			"tool_result" => JsonSerializer.Deserialize(ref reader, ctx.ToolResultEvent),
			"message_chunk" => JsonSerializer.Deserialize(ref reader, ctx.MessageChunkEvent),
			"message_complete" => JsonSerializer.Deserialize(ref reader, ctx.MessageCompleteEvent),
			"thinking_complete" => JsonSerializer.Deserialize(ref reader, ctx.ThinkingCompleteEvent),
			"round_complete" => JsonSerializer.Deserialize(ref reader, ctx.RoundCompleteEvent),
			_ => null
		};
	}
}
