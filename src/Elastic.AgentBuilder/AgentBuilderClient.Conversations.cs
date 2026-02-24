// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading;
using System.Threading.Tasks;
using Elastic.AgentBuilder.Conversations;

namespace Elastic.AgentBuilder;

public partial class AgentBuilderClient
{
	/// <summary> Send a synchronous chat message to an agent. </summary>
	public Task<ConverseResponse> ConverseAsync(ConverseRequest request, CancellationToken ct = default) =>
		PostAsync("/converse", request, Ctx.ConverseRequest, Ctx.ConverseResponse, ct);

	/// <summary> List all conversations. </summary>
	public Task<ListConversationsResponse> ListConversationsAsync(CancellationToken ct = default) =>
		GetAsync("/conversations", Ctx.ListConversationsResponse, ct);

	/// <summary> Get a conversation by its ID. </summary>
	public Task<Conversation> GetConversationAsync(string conversationId, CancellationToken ct = default) =>
		GetAsync($"/conversations/{conversationId}", Ctx.Conversation, ct);

	/// <summary> Delete a conversation by its ID. </summary>
	public Task DeleteConversationAsync(string conversationId, CancellationToken ct = default) =>
		DeleteAsync($"/conversations/{conversationId}", ct);
}
