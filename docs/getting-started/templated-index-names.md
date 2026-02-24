---
navigation_title: Templated index names
---

# Templated index names

Use `NameTemplate` on `[Index<T>]` when the index name contains segments that are only known at runtime -- environment, search variant, tenant, region, or any other dimension. The source generator parses your template and produces a `CreateContext(...)` factory method with typed parameters.

## Why

Statically defined index names work for simple cases, but real deployments often need the same document type written to different indices depending on runtime context:

- `docs-semantic-production` vs `docs-lexical-staging`
- `orders-us-east-1` vs `orders-eu-west-1`
- `articles-team-a-ingest` vs `articles-team-b-search`

Without `NameTemplate`, you'd need to declare one `[Index<T>]` per combination or build index names manually. `NameTemplate` lets you declare the pattern once and resolve it at runtime with full type safety.

## Basic usage

```csharp
[ElasticsearchMappingContext]
[Index<KnowledgeArticle>(
    NameTemplate = "docs-{searchType}-{env}",
    DatePattern = "yyyy.MM.dd.HHmmss"
)]
public static partial class ArticleContext;
```

The generator produces a `CreateContext` method instead of a static `Context` property:

```csharp
// CreateContext(string searchType, string? env = null)
var ctx = ArticleContext.KnowledgeArticle.CreateContext("semantic", env: "production");

ctx.IndexStrategy.WriteTarget   // "docs-semantic-production"
ctx.SearchStrategy.Pattern      // "docs-semantic-production-*"  (auto-derived from DatePattern)
ctx.IndexStrategy.DatePattern   // "yyyy.MM.dd.HHmmss"
```

## Placeholder rules

Placeholders are `{name}` tokens in the template string. The generator classifies them into two categories:

### Custom placeholders

Any placeholder that is not a well-known name becomes a **required** `string` parameter:

```csharp
[Index<T>(NameTemplate = "articles-{team}-{component}")]
// Generates: CreateContext(string component, string team)
```

### Well-known placeholders

The names `{env}`, `{environment}`, and `{namespace}` are treated specially:

- They become **optional** `string?` parameters with a default of `null`
- When `null`, they resolve from environment variables: `DOTNET_ENVIRONMENT` > `ASPNETCORE_ENVIRONMENT` > `ENVIRONMENT` > `"development"`
- They are always placed **last** in the method signature, after all custom placeholders

```csharp
[Index<T>(NameTemplate = "geo-{namespace}")]
// Generates: CreateContext(string? @namespace = null)

var ctx = MyContext.LocationRecord.CreateContext();
// WriteTarget: "geo-development"  (from env variable fallback)

var ctx = MyContext.LocationRecord.CreateContext(@namespace: "us-east-1");
// WriteTarget: "geo-us-east-1"
```

## Name vs NameTemplate

`Name` and `NameTemplate` are mutually exclusive on `[Index<T>]`:

| Property | Resolver exposes | When to use |
|----------|-----------------|-------------|
| `Name = "products"` | `MyContext.Product.Context` (static property) | Fixed index name known at compile time |
| `NameTemplate = "docs-{type}-{env}"` | `MyContext.Product.CreateContext(...)` (factory method) | Index name depends on runtime parameters |

When neither is set, the index name defaults to the type name lowercased (same as `Name`).

## DatePattern with templates

When `DatePattern` is set alongside `NameTemplate`, the generated `CreateContext` method:

1. Sets `IndexStrategy.WriteTarget` to the interpolated name (e.g., `"docs-semantic-production"`)
2. Carries `IndexStrategy.DatePattern` through to the returned context
3. Auto-derives `SearchStrategy.Pattern` as `"{writeTarget}-*"` (e.g., `"docs-semantic-production-*"`)

Use `ResolveIndexName` on the returned context to get the full time-stamped index name:

```csharp
var ctx = ArticleContext.KnowledgeArticle.CreateContext("semantic", env: "prod");
var indexName = ctx.ResolveIndexName(DateTimeOffset.UtcNow);
// "docs-semantic-prod-2026.02.24.143045"
```

Without `DatePattern`, the write target is the final index name and `SearchStrategy.Pattern` is `null`.

## Variants with templates

Combine `Variant` with `NameTemplate` to register the same document type with different template patterns:

```csharp
[ElasticsearchMappingContext]
[Index<KnowledgeArticle>(
    NameTemplate = "docs-{searchType}-{env}",
    DatePattern = "yyyy.MM.dd.HHmmss"
)]
[Index<KnowledgeArticle>(
    NameTemplate = "articles-{team}-{component}",
    Variant = "Multi"
)]
public static partial class ArticleContext;
```

This generates two resolvers:
- `ArticleContext.KnowledgeArticle.CreateContext(string searchType, string? env = null)`
- `ArticleContext.KnowledgeArticleMulti.CreateContext(string component, string team)`

Each returns an independent `ElasticsearchTypeContext` with its own write target and strategy configuration.

## Using the context

The `ElasticsearchTypeContext` returned by `CreateContext` works identically to a static `Context`:

```csharp
var ctx = ArticleContext.KnowledgeArticle.CreateContext("semantic", env: "production");

// Pass to channel options
var options = new IngestChannelOptions<KnowledgeArticle>(transport, ctx);
using var channel = new IngestChannel<KnowledgeArticle>(options);

// Pass to strategies
var strategy = IngestStrategies.Index<KnowledgeArticle>(ctx);

// Resolve helpers
ctx.ResolveIndexName(timestamp)   // time-stamped index name
ctx.ResolveWriteAlias()           // write alias
ctx.ResolveReadTarget()           // read alias or write target
ctx.ResolveSearchPattern()        // search wildcard
```

## Multiple CreateContext calls

Each `CreateContext` call returns an independent context. You can create multiple contexts from the same resolver for different runtime configurations:

```csharp
var semantic = ArticleContext.KnowledgeArticle.CreateContext("semantic", env: "production");
var lexical  = ArticleContext.KnowledgeArticle.CreateContext("lexical", env: "production");

// Use with IncrementalSyncOrchestrator
using var orchestrator = new IncrementalSyncOrchestrator<KnowledgeArticle>(
    transport,
    primary: lexical,
    secondary: semantic
);
```

## Data streams and namespace resolution

Data streams don't use `NameTemplate`. Instead, the `Namespace` property on `[DataStream<T>]` provides equivalent environment-awareness:

```csharp
// Namespace resolved from environment variables when omitted
[DataStream<LogEntry>(Type = "logs", Dataset = "myapp")]

// Explicit namespace
[DataStream<LogEntry>(Type = "logs", Dataset = "myapp", Namespace = "production")]
```

See [mapping context](mapping-context.md) for the full attribute reference.

## Related

- [Mapping context](mapping-context.md): complete attribute reference for `[Index<T>]`, `[DataStream<T>]`, and `[WiredStream<T>]`
- [Single index](../index-management/single-index.md): fixed index naming
- [Incremental sync](../orchestration/incremental-sync.md): coordinating multiple indices from templated contexts
