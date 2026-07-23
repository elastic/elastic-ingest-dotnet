---
navigation_title: Code-driven mappings
---

# Code-driven mappings

Field attributes handle most mapping declarations. For custom analyzers, runtime fields, dynamic templates, or field overrides, implement `IConfigureElasticsearch<T>`.

## The IConfigureElasticsearch interface

```csharp
public interface IConfigureElasticsearch<TDocument> where TDocument : class
{
    AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis);
    MappingsBuilder<TDocument> ConfigureMappings(MappingsBuilder<TDocument> mappings);
    IReadOnlyDictionary<string, string>? IndexSettings { get; }
}
```

| Member | What it produces |
|--------|-----------------|
| `ConfigureAnalysis` | Custom analyzers, tokenizers, filters merged into the settings component template |
| `ConfigureMappings` | Field overrides, dynamic templates, runtime fields merged into the mappings component template |
| `IndexSettings` | Additional key-value pairs in the settings JSON (e.g. `index.default_pipeline`) |

## Priority resolution

The source generator discovers configuration using this priority chain (first match wins):

| Priority | Source |
|----------|--------|
| 1 (highest) | Configuration class specified via `Configuration = typeof(...)` on the target attribute |
| 2 (lowest) | The document type itself implementing `IConfigureElasticsearch<T>` |

## Configuration class

:::{tip}
This is the recommended approach. It keeps your document type clean and allows reuse across multiple contexts.
:::

Reference a dedicated configuration class via the `Configuration` property on the target attribute:

```csharp
[ElasticsearchMappingContext]
[Index<Product>(Name = "products", Configuration = typeof(ProductConfig))]
public static partial class MyContext;

public class ProductConfig : IConfigureElasticsearch<Product>
{
    public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
        analysis
            .Analyzer("product_name_analyzer", a => a
                .Custom()
                .Tokenizer("standard")
                .Filters("lowercase", "asciifolding"))
            .Normalizer("lowercase_normalizer", n => n
                .Custom()
                .Filters("lowercase"));

    public MappingsBuilder<Product> ConfigureMappings(MappingsBuilder<Product> mappings) =>
        mappings.Name(f => f.Analyzer("product_name_analyzer"));

    public IReadOnlyDictionary<string, string>? IndexSettings =>
        new Dictionary<string, string>
        {
            ["index.default_pipeline"] = "product-enrichment"
        };
}
```

## Self-configuring entity

The document type can implement the interface directly when configuration is inherent to the type:

```csharp
public class LogEntry : IConfigureElasticsearch<LogEntry>
{
    [Timestamp]
    public DateTimeOffset Timestamp { get; set; }

    [Text]
    public string Message { get; set; }

    [Keyword]
    public string Level { get; set; }

    public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
        analysis
            .Analyzer("log_message_analyzer", a => a
                .Custom()
                .Tokenizer("standard")
                .Filters("lowercase", "stop"));

    public MappingsBuilder<LogEntry> ConfigureMappings(MappingsBuilder<LogEntry> mappings) =>
        mappings.Message(f => f.Analyzer("log_message_analyzer"));

    public IReadOnlyDictionary<string, string>? IndexSettings => null;
}
```

## Generated MappingsBuilder extensions

The source generator emits strongly-typed extension methods for each property on your document type. These let you override field settings without using strings:

```csharp
public MappingsBuilder<Product> ConfigureMappings(MappingsBuilder<Product> mappings) =>
    mappings
        .Name(f => f.Analyzer("product_name_analyzer"))
        .Category(f => f.Normalizer("lowercase_normalizer"))
        .Description(f => f.Analyzer("english").SearchAnalyzer("english"));
```

Each generated method corresponds to a property on the document class.

## Delegating to shared factories

Configuration classes can delegate to shared factory methods. The source generator follows the call graph to discover analysis component names:

```csharp
public static class SharedAnalysisFactory
{
    public static AnalysisBuilder BuildStandardAnalysis(AnalysisBuilder analysis) =>
        analysis
            .Normalizer("keyword_normalizer", n => n.Custom().Filters("lowercase"))
            .Analyzer("starts_with_analyzer", a => a
                .Custom()
                .Tokenizer("starts_with_tokenizer")
                .Filters("lowercase"))
            .Tokenizer("starts_with_tokenizer", t => t
                .EdgeNGram()
                .MinGram(1)
                .MaxGram(20));
}

public class ProductConfig : IConfigureElasticsearch<Product>
{
    public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
        SharedAnalysisFactory.BuildStandardAnalysis(analysis);

    public MappingsBuilder<Product> ConfigureMappings(MappingsBuilder<Product> mappings) => mappings;
    public IReadOnlyDictionary<string, string>? IndexSettings => null;
}
```

The generator traces calls transitively and emits strongly-typed accessors for all discovered names. See [strongly-typed analysis names](strongly-typed-analysis-names.md) for details.

## Variants with separate configurations

When using `Variant` to register the same document type multiple times, each variant can have its own configuration class:

```csharp
[ElasticsearchMappingContext]
[Index<Article>(Name = "articles-lexical", Configuration = typeof(ArticleLexicalConfig))]
[Index<Article>(Name = "articles-semantic", Variant = "Semantic", Configuration = typeof(ArticleSemanticConfig))]
public static partial class ArticleContext;
```
