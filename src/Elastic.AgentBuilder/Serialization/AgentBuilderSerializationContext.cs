// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.AgentBuilder.Agents;
using Elastic.AgentBuilder.Conversations;
using Elastic.AgentBuilder.Tools;

namespace Elastic.AgentBuilder;

[JsonSerializable(typeof(AgentBuilderTool))]
[JsonSerializable(typeof(ListToolsResponse))]
[JsonSerializable(typeof(EsqlToolConfiguration))]
[JsonSerializable(typeof(IndexSearchToolConfiguration))]
[JsonSerializable(typeof(McpToolConfiguration))]
[JsonSerializable(typeof(WorkflowToolConfiguration))]
[JsonSerializable(typeof(EsqlToolParam))]
[JsonSerializable(typeof(CreateEsqlToolRequest))]
[JsonSerializable(typeof(CreateIndexSearchToolRequest))]
[JsonSerializable(typeof(CreateMcpToolRequest))]
[JsonSerializable(typeof(CreateWorkflowToolRequest))]
[JsonSerializable(typeof(UpdateEsqlToolRequest))]
[JsonSerializable(typeof(UpdateIndexSearchToolRequest))]
[JsonSerializable(typeof(UpdateMcpToolRequest))]
[JsonSerializable(typeof(UpdateWorkflowToolRequest))]
[JsonSerializable(typeof(ExecuteToolRequest))]
[JsonSerializable(typeof(ExecuteToolResponse))]
[JsonSerializable(typeof(ToolResult))]
[JsonSerializable(typeof(TabularData))]
[JsonSerializable(typeof(TabularColumn))]
[JsonSerializable(typeof(QueryResult))]
[JsonSerializable(typeof(AgentBuilderAgent))]
[JsonSerializable(typeof(ListAgentsResponse))]
[JsonSerializable(typeof(AgentConfiguration))]
[JsonSerializable(typeof(AgentToolGroup))]
[JsonSerializable(typeof(CreateAgentRequest))]
[JsonSerializable(typeof(UpdateAgentRequest))]
[JsonSerializable(typeof(Conversation))]
[JsonSerializable(typeof(ListConversationsResponse))]
[JsonSerializable(typeof(ConverseRequest))]
[JsonSerializable(typeof(ConverseResponse))]
[JsonSerializable(typeof(ConverseStep))]
[JsonSerializable(typeof(ConverseMessage))]
[JsonSerializable(typeof(ModelUsage))]
[JsonSourceGenerationOptions(
	PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public sealed partial class AgentBuilderSerializationContext : JsonSerializerContext;
