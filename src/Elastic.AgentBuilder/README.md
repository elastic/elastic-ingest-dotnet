# Elastic.AgentBuilder

A .NET client for the [Elastic Agent Builder Kibana API](https://www.elastic.co/docs/explore-analyze/ai-features/agent-builder/kibana-api).
Create and manage custom AI tools, agents, and conversations in Kibana — and
connect to the Kibana MCP server for use with `Microsoft.Extensions.AI`.

## Why?

Elastic Agent Builder lets you build AI agents that use Elasticsearch data.
This library gives you a strongly-typed C# API to:

- **Define tools and agents in code** and deploy them idempotently via
  hash-based bootstrapping — just like infrastructure-as-code.
- **CRUD all four custom tool types**: ES|QL queries, index search, MCP, and workflow.
- **Execute tools** and consume tabular results with ergonomic column-name access.
- **Drive conversations** with agents programmatically.
- **Connect to the Kibana MCP server** so agent tools are immediately usable
  as `AIFunction` instances with any `IChatClient`.

The library targets `netstandard2.0`, `netstandard2.1`, `net8.0`, and `net10.0`
with full AOT compatibility via `System.Text.Json` source generators.

## Getting Started

`AgentTransportConfiguration` handles all Kibana-specific wiring automatically
(the `kbn-xsrf` header, Cloud ID → Kibana URL resolution, and space prefixes).

### From an Elastic Cloud ID

```csharp
using Elastic.Transport;
using Elastic.AgentBuilder;

var config = new AgentTransportConfiguration("my-cloud-id", new ApiKey("base64key"));
var client = new AgentBuilderClient(config);
```

### From a direct Kibana URL

```csharp
var config = new AgentTransportConfiguration(
    new Uri("https://my-kibana:5601"), new ApiKey("base64key"));
var client = new AgentBuilderClient(config);
```

### Basic authentication

```csharp
var config = new AgentTransportConfiguration(
    "my-cloud-id", new BasicAuthentication("user", "password"));
var client = new AgentBuilderClient(config);
```

### Kibana Spaces

Set the `Space` property to scope all API calls to a specific Kibana space.
All requests will be prefixed with `/s/{Space}/api/agent_builder/...`:

```csharp
var config = new AgentTransportConfiguration("my-cloud-id", new ApiKey("base64key"))
{
    Space = "my-space"
};
var client = new AgentBuilderClient(config);
```

### Using AgentTransport directly

If you need an `ITransport` instance (for example, to share with
`Elastic.Extensions.AI` or custom code), create an `AgentTransport`:

```csharp
var config = new AgentTransportConfiguration("my-cloud-id", new ApiKey("base64key"))
{
    Space = "analytics"
};
var transport = new AgentTransport(config);

// Pass to the client
var client = new AgentBuilderClient(transport);

// Or use the transport directly — kbn-xsrf is already wired
```

### Advanced: raw ITransport

For full control you can pass any `ITransport` directly.
You are responsible for setting the `kbn-xsrf` header yourself:

```csharp
var transport = new DistributedTransport(
    new TransportConfigurationDescriptor(new SingleNodePool(new Uri("https://my-kibana:5601")))
        .Authentication(new ApiKey("base64key"))
        .GlobalHeaders(new NameValueCollection { { "kbn-xsrf", "true" } }));

var client = new AgentBuilderClient(transport, space: "my-space");
```

## Creating Tools

### ES|QL Tool

Define a parameterized ES|QL query that agents can invoke:

```csharp
using Elastic.AgentBuilder.Tools;

var tool = await client.CreateToolAsync(new CreateEsqlToolRequest
{
    Id = "top-books-by-pages",
    Description = "Find the books with the most pages",
    Tags = ["analytics", "books"],
    Configuration = new EsqlToolConfiguration
    {
        Query = "FROM books | SORT page_count DESC | LIMIT ?limit",
        Params = new Dictionary<string, EsqlToolParam>
        {
            ["limit"] = new()
            {
                Type = "integer",
                Description = "Maximum number of results to return",
                DefaultValue = 10
            }
        }
    }
});
```

### Index Search Tool

Scope an agent's search to specific index patterns:

```csharp
var searchTool = await client.CreateToolAsync(new CreateIndexSearchToolRequest
{
    Id = "search-application-logs",
    Description = "Search application logs for errors and patterns",
    Tags = ["observability"],
    Configuration = new IndexSearchToolConfiguration
    {
        IndexPattern = "logs-myapp-*",
        RowLimit = 100,
        CustomInstructions = "Always include @timestamp and log.level in results"
    }
});
```

### MCP Tool

Connect to an external MCP server through Kibana:

```csharp
var mcpTool = await client.CreateToolAsync(new CreateMcpToolRequest
{
    Id = "github-issues",
    Description = "Search GitHub issues via MCP",
    Configuration = new McpToolConfiguration
    {
        ConnectorId = "my-mcp-connector",
        ToolName = "search_issues"
    }
});
```

### Workflow Tool

Trigger an Elastic Workflow:

```csharp
var workflowTool = await client.CreateToolAsync(new CreateWorkflowToolRequest
{
    Id = "run-remediation",
    Description = "Run the incident remediation workflow",
    Configuration = new WorkflowToolConfiguration
    {
        WorkflowId = "incident-remediation-v2"
    }
});
```

## Managing Tools

```csharp
// List all tools (built-in and custom)
var all = await client.ListToolsAsync();
foreach (var t in all.Results)
    Console.WriteLine($"{t.Id} ({t.Type}) — {t.Description}");

// Get a specific tool and inspect its typed configuration
var tool = await client.GetToolAsync("top-books-by-pages");
var esqlConfig = tool.AsEsql();
Console.WriteLine($"Query: {esqlConfig?.Query}");

// Update
await client.UpdateToolAsync("top-books-by-pages", new UpdateEsqlToolRequest
{
    Description = "Find the longest books in the catalogue",
    Configuration = new EsqlToolConfiguration
    {
        Query = "FROM books | SORT page_count DESC | LIMIT ?limit",
        Params = new Dictionary<string, EsqlToolParam>
        {
            ["limit"] = new() { Type = "integer", Description = "Max results" }
        }
    }
});

// Delete
await client.DeleteToolAsync("top-books-by-pages");
```

## Executing Tools

Execute a tool and consume the tabular results:

```csharp
var result = await client.ExecuteToolAsync(new ExecuteToolRequest
{
    ToolId = "top-books-by-pages",
    ToolParams = new() { ["limit"] = JsonSerializer.SerializeToElement(5) }
});

foreach (var item in result.Results)
{
    if (item.AsTabularData() is { } tabular)
    {
        Console.WriteLine($"Source: {tabular.Source}, Columns: {tabular.Columns.Count}");
        for (var i = 0; i < tabular.Values.Count; i++)
        {
            var row = tabular.Row(i);
            Console.WriteLine($"  {row.GetString("title")} — {row.GetInt32("page_count")} pages");
        }
    }

    if (item.AsQuery() is { } query)
        Console.WriteLine($"Generated ES|QL: {query.Esql}");
}
```

## Creating Agents

```csharp
using Elastic.AgentBuilder.Agents;

var agent = await client.CreateAgentAsync(new CreateAgentRequest
{
    Id = "books-research-agent",
    Name = "Books Research Assistant",
    Description = "Helps users explore and analyse the book catalogue",
    AvatarColor = "#BFDBFF",
    AvatarSymbol = "📚",
    Labels = ["books", "research"],
    Configuration = new AgentConfiguration
    {
        Instructions = """
            You are a helpful assistant that answers questions about our book catalogue.
            Use the top-books-by-pages tool when users ask about long or popular books.
            Use the search tool for general queries.
            Always cite your sources.
            """,
        Tools =
        [
            new AgentToolGroup
            {
                ToolIds = ["top-books-by-pages", "search-application-logs", "platform.core.search"]
            }
        ]
    }
});
```

## Conversations

Drive a conversation with an agent:

```csharp
using Elastic.AgentBuilder.Conversations;

// Start a new conversation
var response = await client.ConverseAsync(new ConverseRequest
{
    Input = "What are the three longest books in our collection?",
    AgentId = "books-research-agent"
});

Console.WriteLine(response.Response?.Message);
Console.WriteLine($"Tokens: {response.ModelUsage?.InputTokens} in / {response.ModelUsage?.OutputTokens} out");

// Continue the conversation
var followUp = await client.ConverseAsync(new ConverseRequest
{
    Input = "Who wrote them?",
    AgentId = "books-research-agent",
    ConversationId = response.ConversationId
});

// List and manage conversations
var conversations = await client.ListConversationsAsync();
await client.DeleteConversationAsync(response.ConversationId);
```

## Bootstrapping — Infrastructure as Code

The bootstrapper computes a SHA-256 hash of each definition and stores it in the
resource's `tags` (tools) or `labels` (agents). On subsequent runs, a resource is
only updated when its hash has changed — making deployments fast and idempotent.

```csharp
var bootstrapper = new AgentBuilderBootstrapper(client);

var definition = new BootstrapDefinition
{
    EsqlTools =
    [
        new CreateEsqlToolRequest
        {
            Id = "top-books-by-pages",
            Description = "Find books with the most pages",
            Configuration = new EsqlToolConfiguration
            {
                Query = "FROM books | SORT page_count DESC | LIMIT ?limit",
                Params = new Dictionary<string, EsqlToolParam>
                {
                    ["limit"] = new() { Type = "integer", Description = "Max results" }
                }
            }
        }
    ],
    IndexSearchTools =
    [
        new CreateIndexSearchToolRequest
        {
            Id = "search-logs",
            Description = "Search application logs",
            Configuration = new IndexSearchToolConfiguration { IndexPattern = "logs-*" }
        }
    ],
    Agents =
    [
        new CreateAgentRequest
        {
            Id = "ops-agent",
            Name = "Operations Agent",
            Configuration = new AgentConfiguration
            {
                Instructions = "You help the operations team investigate issues.",
                Tools = [new AgentToolGroup { ToolIds = ["search-logs", "platform.core.search"] }]
            }
        }
    ]
};

// BootstrapMethod.Failure throws on errors; .Silent swallows them; .None skips entirely
await bootstrapper.BootstrapAsync(BootstrapMethod.Failure, definition);

// Safe to call on every application startup — unchanged resources are skipped
```

## Using with Elastic.Esql — LINQ-generated queries

[Elastic.Esql](https://github.com/elastic/esql-dotnet) lets you write LINQ and
get ES|QL query strings with full IntelliSense and compile-time checking. You
can feed the generated queries straight into Agent Builder tool definitions.

### Inline queries

```csharp
using Elastic.Esql.Core;
using Elastic.AgentBuilder.Tools;

var query = new EsqlQueryable<Book>()
    .Where(b => b.PageCount > 200)
    .OrderByDescending(b => b.PageCount)
    .Take(50)
    .ToString();
// "FROM books | WHERE page_count > 200 | SORT page_count DESC | LIMIT 50"

var tool = await client.CreateToolAsync(new CreateEsqlToolRequest
{
    Id = "long-books",
    Description = "Find books with more than 200 pages",
    Configuration = new EsqlToolConfiguration { Query = query }
});
```

### Parameterized queries

Elastic.Esql natively captures C# variables and turns them into named
parameters when you call `ToEsqlString(inlineParameters: false)`. Use this to
define the query shape in LINQ while exposing parameters the agent supplies at
runtime:

```csharp
var minPages = 100;
var limit = 25;

var queryable = new EsqlQueryable<Book>()
    .Where(b => b.PageCount > minPages)
    .OrderByDescending(b => b.PageCount)
    .Take(limit);

var esql = queryable.ToEsqlString(inlineParameters: false);
// "FROM books | WHERE page_count > ?minPages | SORT page_count DESC | LIMIT ?limit"

await client.CreateToolAsync(new CreateEsqlToolRequest
{
    Id = "top-books",
    Description = "Find books above a page threshold",
    Configuration = new EsqlToolConfiguration
    {
        Query = esql,
        Params = new Dictionary<string, EsqlToolParam>
        {
            ["minPages"] = new()
            {
                Type = "integer",
                Description = "Minimum number of pages",
                DefaultValue = 100
            },
            ["limit"] = new()
            {
                Type = "integer",
                Description = "Maximum number of results to return",
                DefaultValue = 25
            }
        }
    }
});
```

This gives you compile-time field name resolution and IntelliSense for the
query logic, while the agent supplies dynamic parameter values at runtime.

## Microsoft.Extensions.AI & MCP

For `IChatClient` integration, MCP tool discovery, and DI extensions see the
companion package **[Elastic.Extensions.AI](https://www.nuget.org/packages/Elastic.Extensions.AI)**.
