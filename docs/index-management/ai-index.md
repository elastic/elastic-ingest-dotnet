---
navigation_title: AI index
---

# AI index

An AI index is an index or data stream that follows the Elasticsearch base schema for AI agent context (`ai-index@mappings`, Elasticsearch 9.6 and later). Setting `AiIndex = true` on `[Index<T>]` or `[DataStream<T>]` makes the generated mapping and template follow that schema while keeping your own fields.

## Configuration

Derive from `AiIndexDocument`, add your own fields, and set `AiIndex = true`:

```csharp
public class Article : AiIndexDocument
{
    [Keyword]
    [JsonPropertyName("author")]
    public string? Author { get; set; }
}

[ElasticsearchMappingContext]
[Index<Article>(Name = "ai-index-idx-articles", AiIndex = true)]
public static partial class ArticleContext;

var article = new Article
{
    Id = "article-1",
    Title = "A title",
    Content = "The text of the article",
    Author = "someone"
};
```

`AiIndexDocument` has a property for each base field, with the JSON name and field type of `ai-index@mappings`: `Timestamp`, `Id`, `Type`, `Title`, `Description`, `Content`, `Tags`, `Attributes`, `UpdatedAt`, `ExpiresAt`, `References` and `Governance`. Empty fields are not written. `Timestamp` defaults to the time the instance is created.

### Roles and overrides

The base class declares no roles except `[Id]` on `Id`. Add the roles your channel needs in the derived type. A data stream needs `[Timestamp]`, which you put on an override of `Timestamp`. A property of the derived type with the same name wins, so an override also changes the mapping. Repeat `[JsonPropertyName]` on an override: only the attributes of the most derived property count.

```csharp
public class Article : AiIndexDocument
{
    [Text(Analyzer = "standard")]
    [JsonPropertyName("title")]
    public override string? Title { get; set; }

    [Timestamp]
    [JsonPropertyName("@timestamp")]
    public override DateTimeOffset Timestamp { get; set; }
}
```

A derived type with its own `[Id]` property, for example a URL, uses that property for `_id` instead of `Id`.

### Without the base class

A type that does not derive from `AiIndexDocument` can still set `AiIndex = true`. The base fields it does not map are added to the mapping, with the same rules as below. Fields that the type defines itself keep their definition, so a type with a text `tags` field keeps it.

## What it changes

**Mapping.** The base fields are added next to your own: `@timestamp`, `id`, `type`, `title`, `description` and `content` (text with a `.semantic` `semantic_text` multi-field), `tags`, `attributes`, `references`, `updated_at`, `expires_at` and `governance`. The merge is additive: a field you already map keeps its definition, and a partly mapped field only gains what it lacks. In the example `Title` stays text with its `.keyword` multi-field and gains `.semantic`.

The library does not fill in the base fields. Set the properties of `AiIndexDocument` yourself, or, for a type that does not derive from it, add properties with the JSON names you want, for example `[JsonPropertyName("content")]`.

**Template.** The index template composes `ai-index@mappings` before the generated mappings and `ai-index@custom` after the generated settings. Both are optional: a cluster without them (before 9.6, or without `ai-index@custom`) uses the copy of the base mapping that is already in the generated mapping. The template priority is 501, because Elasticsearch's own `ai-index-idx-*` and `ai-index-ds-*` templates use 500 and only the highest priority template that matches an index applies.

## Limits

- The base mapping is an embedded copy of registry version 4. A later change to `ai-index@mappings` on the cluster is composed in, but only reaches a generated mapping when the library is updated.
- The data stream defaults of AI indices (90 day retention, `@timestamp` descending sort) are not applied. Configure retention through the bootstrap strategy.
- Legacy channels (`IndexChannel`, `DataStreamChannel`) do not read the option. Use `IngestChannel` or the orchestrators.
