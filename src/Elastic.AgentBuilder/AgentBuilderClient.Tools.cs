// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading;
using System.Threading.Tasks;
using Elastic.AgentBuilder.Tools;

namespace Elastic.AgentBuilder;

public partial class AgentBuilderClient
{
	private static readonly AgentBuilderSerializationContext Ctx = AgentBuilderSerializationContext.Default;

	/// <summary> List all available tools. </summary>
	public Task<ListToolsResponse> ListToolsAsync(CancellationToken ct = default) =>
		GetAsync("/tools", Ctx.ListToolsResponse, ct);

	/// <summary> Get a tool by its ID. </summary>
	public Task<AgentBuilderTool> GetToolAsync(string toolId, CancellationToken ct = default) =>
		GetAsync($"/tools/{toolId}", Ctx.AgentBuilderTool, ct);

	/// <summary> Create an ES|QL tool. </summary>
	public Task<AgentBuilderTool> CreateToolAsync(CreateEsqlToolRequest request, CancellationToken ct = default) =>
		PostAsync("/tools", request, Ctx.CreateEsqlToolRequest, Ctx.AgentBuilderTool, ct);

	/// <summary> Create an index search tool. </summary>
	public Task<AgentBuilderTool> CreateToolAsync(CreateIndexSearchToolRequest request, CancellationToken ct = default) =>
		PostAsync("/tools", request, Ctx.CreateIndexSearchToolRequest, Ctx.AgentBuilderTool, ct);

	/// <summary> Create an MCP tool. </summary>
	public Task<AgentBuilderTool> CreateToolAsync(CreateMcpToolRequest request, CancellationToken ct = default) =>
		PostAsync("/tools", request, Ctx.CreateMcpToolRequest, Ctx.AgentBuilderTool, ct);

	/// <summary> Create a workflow tool. </summary>
	public Task<AgentBuilderTool> CreateToolAsync(CreateWorkflowToolRequest request, CancellationToken ct = default) =>
		PostAsync("/tools", request, Ctx.CreateWorkflowToolRequest, Ctx.AgentBuilderTool, ct);

	/// <summary> Update an ES|QL tool. </summary>
	public Task<AgentBuilderTool> UpdateToolAsync(string toolId, UpdateEsqlToolRequest request, CancellationToken ct = default) =>
		PutAsync($"/tools/{toolId}", request, Ctx.UpdateEsqlToolRequest, Ctx.AgentBuilderTool, ct);

	/// <summary> Update an index search tool. </summary>
	public Task<AgentBuilderTool> UpdateToolAsync(string toolId, UpdateIndexSearchToolRequest request, CancellationToken ct = default) =>
		PutAsync($"/tools/{toolId}", request, Ctx.UpdateIndexSearchToolRequest, Ctx.AgentBuilderTool, ct);

	/// <summary> Update an MCP tool. </summary>
	public Task<AgentBuilderTool> UpdateToolAsync(string toolId, UpdateMcpToolRequest request, CancellationToken ct = default) =>
		PutAsync($"/tools/{toolId}", request, Ctx.UpdateMcpToolRequest, Ctx.AgentBuilderTool, ct);

	/// <summary> Update a workflow tool. </summary>
	public Task<AgentBuilderTool> UpdateToolAsync(string toolId, UpdateWorkflowToolRequest request, CancellationToken ct = default) =>
		PutAsync($"/tools/{toolId}", request, Ctx.UpdateWorkflowToolRequest, Ctx.AgentBuilderTool, ct);

	/// <summary> Delete a tool by its ID. </summary>
	public Task DeleteToolAsync(string toolId, CancellationToken ct = default) =>
		DeleteAsync($"/tools/{toolId}", ct);

	/// <summary> Execute a tool with parameters. </summary>
	public Task<ExecuteToolResponse> ExecuteToolAsync(ExecuteToolRequest request, CancellationToken ct = default) =>
		PostAsync("/tools/_execute", request, Ctx.ExecuteToolRequest, Ctx.ExecuteToolResponse, ct);
}
