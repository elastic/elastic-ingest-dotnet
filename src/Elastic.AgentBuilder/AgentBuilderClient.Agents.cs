// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading;
using System.Threading.Tasks;
using Elastic.AgentBuilder.Agents;

namespace Elastic.AgentBuilder;

public partial class AgentBuilderClient
{
	/// <summary> List all agents. </summary>
	public Task<ListAgentsResponse> ListAgentsAsync(CancellationToken ct = default) =>
		GetAsync("/agents", Ctx.ListAgentsResponse, ct);

	/// <summary> Get an agent by its ID. </summary>
	public Task<AgentBuilderAgent> GetAgentAsync(string agentId, CancellationToken ct = default) =>
		GetAsync($"/agents/{agentId}", Ctx.AgentBuilderAgent, ct);

	/// <summary> Create a new agent. </summary>
	public Task<AgentBuilderAgent> CreateAgentAsync(CreateAgentRequest request, CancellationToken ct = default) =>
		PostAsync("/agents", request, Ctx.CreateAgentRequest, Ctx.AgentBuilderAgent, ct);

	/// <summary> Update an existing agent. </summary>
	public Task<AgentBuilderAgent> UpdateAgentAsync(string agentId, UpdateAgentRequest request, CancellationToken ct = default) =>
		PutAsync($"/agents/{agentId}", request, Ctx.UpdateAgentRequest, Ctx.AgentBuilderAgent, ct);

	/// <summary> Delete an agent by its ID. </summary>
	public Task DeleteAgentAsync(string agentId, CancellationToken ct = default) =>
		DeleteAsync($"/agents/{agentId}", ct);
}
