---
navigation_title: AI index
---

# AI index

An AI index is an index or data stream that follows the Elasticsearch base schema for AI agent context (`ai-index@mappings`, Elasticsearch 9.6 and later). Setting `AiIndex = true` on `[Index<T>]` or `[DataStream<T>]` makes the generated mapping and template follow that schema while keeping your own fields.

## Configuration

```csharp
public class Article
{
    [Id]
    [Keyword]
    [JsonPropertyName("id")]
    public string Url { get; set; }

    public string Title { get; set; }

    [JsonPropertyName("content")]
    public string Body { get; set; }

    [Keyword]
    public string Section { get; set; }
}

[ElasticsearchMappingContext]
[Index<Article>(Name = "ai-index-idx-articles", AiIndex = true)]
public static partial class ArticleContext;
```

## What it changes

**Mapping.** The base fields are added next to your own: `@timestamp`, `id`, `type`, `title`, `description` and `content` (text with a `.semantic` `semantic_text` multi-field), `tags`, `attributes`, `references`, `updated_at`, `expires_at` and `governance`. The merge is additive: a field you already map keeps its definition, and a partly mapped field only gains what it lacks. In the example `Title` stays text with its `.keyword` multi-field and gains `.semantic`.

The base fields are ordinary properties. The library does not fill them in, so give your document type properties for the ones you want, for example `[JsonPropertyName("content")]` above.

**Template.** The index template composes `ai-index@mappings` before the generated mappings and `ai-index@custom` after the generated settings. Both are optional: a cluster without them (before 9.6, or without `ai-index@custom`) uses the copy of the base mapping that is already in the generated mapping. The template priority is 501, because Elasticsearch's own `ai-index-idx-*` and `ai-index-ds-*` templates use 500 and only the highest priority template that matches an index applies.

## Limits

- The base mapping is an embedded copy of registry version 4. A later change to `ai-index@mappings` on the cluster is composed in, but only reaches a generated mapping when the library is updated.
- The data stream defaults of AI indices (90 day retention, `@timestamp` descending sort) are not applied. Configure retention through the bootstrap strategy.
- Legacy channels (`IndexChannel`, `DataStreamChannel`) do not read the option. Use `IngestChannel` or the orchestrators.
