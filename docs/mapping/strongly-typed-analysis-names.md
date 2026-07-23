---
navigation_title: Strongly-typed analysis names
---

# Strongly-typed analysis names

When you define custom analyzers, tokenizers, or filters via `ConfigureAnalysis`, the source generator emits accessor classes that expose those names as properties. This gives you compile-time verification instead of runtime failures from typos.

## The problem

A typo in an analyzer reference silently produces a broken mapping:

```csharp
// No compile-time check. Fails at index time.
[Text(Analyzer = "product_naem_analyzer")]  // typo!
public string Name { get; set; }
```

## The solution

The generator emits a static class with properties for each custom analysis component:

```csharp
// Generated: ProductAnalysis
public static class ProductAnalysis
{
    public sealed class AnalyzersAccessor : Elastic.Mapping.Analysis.AnalyzersAccessor
    {
        public string ProductNameAnalyzer => "product_name_analyzer";
    }

    public sealed class NormalizersAccessor : Elastic.Mapping.Analysis.NormalizersAccessor
    {
        public string LowercaseNormalizer => "lowercase_normalizer";
    }

    public static readonly AnalyzersAccessor Analyzers = new();
    public static readonly TokenizersAccessor Tokenizers = new();
    public static readonly TokenFiltersAccessor TokenFilters = new();
    public static readonly CharFiltersAccessor CharFilters = new();
    public static readonly NormalizersAccessor Normalizers = new();
}
```

## Using the accessor

Reference analysis components through the generated accessor:

```csharp
// Compile-time verified. A typo here is a build error.
mappings.Name(f => f.Analyzer(ProductAnalysis.Analyzers.ProductNameAnalyzer));

// Built-in names are also available through the same accessor
mappings.Title(f => f.Analyzer(ProductAnalysis.Analyzers.Standard));

// Language analyzers
mappings.Body(f => f.Analyzer(ProductAnalysis.Analyzers.Language.English));
```

Each accessor class inherits from a base class that exposes all built-in Elasticsearch names. Custom names are added as additional properties.

## Naming convention

Component names are converted from snake_case to PascalCase:

| Analysis component name | Generated property name |
|------------------------|------------------------|
| `product_name_analyzer` | `ProductNameAnalyzer` |
| `keyword_normalizer` | `KeywordNormalizer` |
| `starts_with_tokenizer` | `StartsWithTokenizer` |
| `english_stop` | `EnglishStop` |

## Built-in names on the accessor

Every generated accessor inherits built-in Elasticsearch names alongside your custom ones:

```csharp
// Custom analyzer
ProductAnalysis.Analyzers.ProductNameAnalyzer   // "product_name_analyzer"

// Built-in analyzer (inherited from base)
ProductAnalysis.Analyzers.Standard              // "standard"
ProductAnalysis.Analyzers.Whitespace            // "whitespace"
ProductAnalysis.Analyzers.Language.English       // "english"

// Built-in token filters (inherited from base)
ProductAnalysis.TokenFilters.Lowercase          // "lowercase"
ProductAnalysis.TokenFilters.AsciiFolding       // "asciifolding"
```

One accessor is your single source of truth for all analysis component names.

## Advanced: base-type anchoring and delegation

:::{dropdown} How the generator handles shared factories and inheritance
### Base-type anchored accessors

When your `ConfigureAnalysis` method delegates to a shared factory (rather than defining analysis inline), the generator anchors the accessor to the nearest user-defined base type. This enables generic code constrained on the base type to reference analysis names without knowing the concrete document type.

**Example:**

```csharp
public class SearchBaseDocument
{
    [Id] [Keyword]
    public string Id { get; set; }
}

public class SearchArticle : SearchBaseDocument { /* ... */ }
public class SearchProduct : SearchBaseDocument { /* ... */ }

public static class SearchAnalysisFactory
{
    public const string KeywordNormalizerName = "keyword_normalizer";

    public static AnalysisBuilder BuildBaseAnalysis(AnalysisBuilder analysis) =>
        analysis
            .Normalizer(KeywordNormalizerName, n => n.Custom().Filters("lowercase"))
            .Analyzer("starts_with_analyzer", a => a
                .Custom().Tokenizer("starts_with_tokenizer").Filters("lowercase"))
            .Tokenizer("starts_with_tokenizer", t => t
                .EdgeNGram().MinGram(1).MaxGram(20));
}

public class SearchArticleConfig : IConfigureElasticsearch<SearchArticle>
{
    public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
        SearchAnalysisFactory.BuildBaseAnalysis(analysis);
    // ...
}
```

Because both configs delegate to the same factory and share a base type, the generator emits a single `SearchBaseDocumentAnalysis` class usable in generic code:

```csharp
// Static accessor (anywhere)
var name = SearchBaseDocumentAnalysis.Analyzers.StartsWithAnalyzer;

// Generic-constrained extension (in MappingsBuilder context)
public MappingsBuilder<T> Configure<T>(MappingsBuilder<T> m) where T : SearchBaseDocument
{
    var name = m.Analyzers().StartsWithAnalyzer;  // same value
    return m;
}
```

### Transitive delegation following

The generator follows method calls transitively. If `ConfigureAnalysis` calls method A which calls method B, all analysis components in both A and B are discovered.

### Const resolution

The generator resolves `const` field references when extracting names:

```csharp
public const string KeywordNormalizerName = "keyword_normalizer";

// This works. The generator resolves the const to "keyword_normalizer".
analysis.Normalizer(KeywordNormalizerName, n => n.Custom().Filters("lowercase"))
```

### Union merging across registrations

When multiple registered types share the same base-type anchor but delegate to different factory methods, the generated accessor contains the **union** of all discovered components across all registrations.
:::
