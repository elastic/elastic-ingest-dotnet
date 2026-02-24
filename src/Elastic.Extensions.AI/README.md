# Elastic.Extensions.AI

[Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai) integration
for the [Elastic Agent Builder](https://www.elastic.co/docs/explore-analyze/ai-features/agent-builder/kibana-api).

Turns Kibana agents into standard `IChatClient` instances and connects to the
Kibana MCP server — so agent tools are immediately usable with any `IChatClient`
pipeline.

## Why?

`Microsoft.Extensions.AI` defines a unified abstraction layer (`IChatClient`,
`IEmbeddingGenerator`, `AIFunction`) for working with AI services in .NET.
This package bridges Elastic Agent Builder into that ecosystem:

- **`ElasticAgentChatClient : IChatClient`** — wrap any Kibana agent as a
  standard chat client, composable with `ChatClientBuilder` middleware
  (function invocation, OpenTelemetry, caching, rate limiting).
- **MCP tool discovery** — connect to the Kibana MCP server and get back
  `McpClientTool` instances that implement `AIFunction`, ready to use with
  any `IChatClient`.
- **DI extensions** — one-liner registration for ASP.NET Core / Generic Host apps.

## Quick Start — IChatClient

```csharp
using Elastic.Transport;
using Elastic.AgentBuilder;
using Elastic.Extensions.AI;
using Microsoft.Extensions.AI;

var config = new AgentTransportConfiguration("my-cloud-id", new ApiKey("key"));
var agentClient = new AgentBuilderClient(config);

// Wrap as IChatClient
IChatClient chatClient = new ElasticAgentChatClient(agentClient, agentId: "my-agent");

// Use with the standard M.E.AI API
var response = await chatClient.GetResponseAsync("What are our top books?");
Console.WriteLine(response.Messages.Last().Text);
Console.WriteLine($"Tokens: {response.Usage?.InputTokenCount} in / {response.Usage?.OutputTokenCount} out");
```

### With Kibana Spaces

```csharp
var config = new AgentTransportConfiguration("my-cloud-id", new ApiKey("key"))
{
    Space = "analytics"
};
var agentClient = new AgentBuilderClient(config);
IChatClient chatClient = new ElasticAgentChatClient(agentClient, agentId: "my-agent");
```

## Quick Start — MCP Tools

```csharp
using Elastic.Extensions.AI;
using ModelContextProtocol.Client;

// Connect to the Kibana MCP endpoint
var mcpClient = await AgentBuilderMcp.CreateClientAsync(
    new Uri("https://my-kibana:5601"), "base64apikey");

// List tools — each implements AIFunction
IList<McpClientTool> tools = await mcpClient.ListToolsAsync();

// Pass to any IChatClient
var response = await chatClient.GetResponseAsync(
    "Find the longest book",
    new ChatOptions { Tools = [.. tools] });
```

## Dependency Injection

```csharp
using Elastic.Extensions.AI;

// In Program.cs — using a Cloud ID
builder.Services.AddElasticAgentBuilder(
    new AgentTransportConfiguration("my-cloud-id", new ApiKey("base64key")),
    agentId: "my-agent");

// Inject anywhere
public class MyService(IChatClient chatClient, AgentBuilderClient agentClient)
{
    public async Task<string> AskAsync(string question)
    {
        var response = await chatClient.GetResponseAsync(question);
        return response.Messages.Last().Text;
    }
}
```

With a direct Kibana URL and space:

```csharp
builder.Services.AddElasticAgentBuilder(
    new AgentTransportConfiguration(new Uri("https://my-kibana:5601"), new ApiKey("key"))
    {
        Space = "my-space"
    },
    agentId: "my-agent");
```

## ChatClientBuilder Middleware

Because `ElasticAgentChatClient` implements `IChatClient`, it composes with
the full `ChatClientBuilder` pipeline:

```csharp
IChatClient chatClient = new ChatClientBuilder(
        new ElasticAgentChatClient(agentClient, "my-agent"))
    .UseFunctionInvocation()   // auto-invoke MCP tools
    .UseOpenTelemetry()        // traces + metrics
    .Build();
```

## Conversations

The `ConversationId` from `ChatOptions` / `ChatResponse` maps directly to the
Kibana conversation ID, enabling multi-turn conversations:

```csharp
var r1 = await chatClient.GetResponseAsync("What books do we have?");
Console.WriteLine(r1.Messages.Last().Text);

// Continue the conversation
var r2 = await chatClient.GetResponseAsync(
    [new ChatMessage(ChatRole.User, "Who wrote the longest one?")],
    new ChatOptions { ConversationId = r1.ConversationId });
Console.WriteLine(r2.Messages.Last().Text);
```
