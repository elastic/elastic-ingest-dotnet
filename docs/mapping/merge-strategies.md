---
navigation_title: Mapping merge
---

# Mapping and analysis merge

When multiple document types share analysis components or field structures, you can merge one type's generated mapping and analysis into another. This avoids duplicating configuration and enables superset indices (one index holding multiple document shapes).

:::{important}
**Conflict rule:** The local definition always wins. A name or path already present on the builder is never overwritten by a merge source. No exception is thrown for conflicts.
:::

## MappingsBuilder.Merge

### Merge from a static resolver

Merges all generated fields from another type's context:

```csharp
public MappingsBuilder<Product> ConfigureMappings(MappingsBuilder<Product> mappings) =>
    mappings.Merge<OtherDocument>(MyContext.OtherDocument);
```

Every field path present on `OtherDocument` that is **not** already present on `Product` is added.

### Merge with overrides

Apply additional configuration to the merged type before merging:

```csharp
public MappingsBuilder<Product> ConfigureMappings(MappingsBuilder<Product> mappings) =>
    mappings.Merge<OtherDocument>(
        MyContext.OtherDocument,
        other => other.Title(f => f.Analyzer("english")));
```

### Merge from a resolved context

When the source uses `NameTemplate` and has a `CreateContext(...)` method:

```csharp
var otherContext = OtherMappingContext.Article.CreateContext("search", "prod");

mappings.Merge(otherContext);
```

## AnalysisBuilder.Merge

Include another type's analysis components in the current builder:

```csharp
public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
    analysis
        .Analyzer("local_analyzer", a => a.Custom().Tokenizer("standard").Filters("lowercase"))
        .Merge<OtherDocument>(MyContext.OtherDocument);
```

Same conflict rule: names already defined locally are never overwritten.

## Use case: superset index

Combine multiple document types into a single index:

```csharp
[ElasticsearchMappingContext]
[Index<UnifiedDocument>(Name = "content", Configuration = typeof(UnifiedConfig))]
public static partial class ContentContext;

public class UnifiedConfig : IConfigureElasticsearch<UnifiedDocument>
{
    public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
        analysis
            .Merge<Article>(ArticleContext.Article)
            .Merge<BlogPost>(BlogContext.BlogPost);

    public MappingsBuilder<UnifiedDocument> ConfigureMappings(MappingsBuilder<UnifiedDocument> mappings) =>
        mappings
            .Merge<Article>(ArticleContext.Article)
            .Merge<BlogPost>(BlogContext.BlogPost);

    public IReadOnlyDictionary<string, string>? IndexSettings => null;
}
```

The `UnifiedDocument` index gets fields from both `Article` and `BlogPost`, plus any fields declared directly on `UnifiedDocument`.

## AddField and AddProperty

In addition to `Merge`, `MappingsBuilder` provides methods for adding individual fields not present on the CLR type.

### AddField: add a multi-field (sub-field)

Adds a sub-field under an existing leaf field's `fields` container:

```csharp
mappings.AddField("title.raw", f => f.Keyword().IgnoreAbove(256));
// Result: "title": { "type": "text", "fields": { "raw": { "type": "keyword" } } }
```

### AddProperty: add a sub-property

Adds a sub-property under an existing object field's `properties` container:

```csharp
mappings.AddProperty("address.zip_code", f => f.Keyword());
// Result: "address": { "type": "object", "properties": { "zip_code": { "type": "keyword" } } }
```

:::{note}
The generator reports diagnostics (`EMAP001`, `EMAP002`) if you use the wrong method. `AddField` is for leaf-type parents (text, keyword). `AddProperty` is for object/nested parents.
:::

## Order of operations

When the channel bootstraps, mappings are composed in this order:

1. Generated base mapping from field attributes on the CLR type
2. `ConfigureMappings` overrides (`AddField`, `AddProperty`, generated extension methods)
3. Merge sources (additive, local always wins)
4. Dynamic templates (appended in declaration order)
5. Runtime fields (appended to the `runtime` section)
