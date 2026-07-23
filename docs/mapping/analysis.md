---
navigation_title: Analysis configuration
---

# Analysis configuration

Custom analysis (analyzers, tokenizers, token filters, character filters, and normalizers) is configured through the `AnalysisBuilder` fluent API inside your `ConfigureAnalysis` method.

:::{tip}
When you define custom analysis components, the source generator emits strongly-typed accessor classes so you can reference them without magic strings. See [strongly-typed analysis names](strongly-typed-analysis-names.md).
:::

## AnalysisBuilder fluent API

`AnalysisBuilder` provides five registration methods, one per analysis component category:

```csharp
public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
    analysis
        .Analyzer("my_analyzer", a => a
            .Custom()
            .Tokenizer("standard")
            .Filters("lowercase", "asciifolding", "my_stop"))
        .Tokenizer("my_tokenizer", t => t
            .EdgeNGram()
            .MinGram(2)
            .MaxGram(20))
        .TokenFilter("my_stop", f => f
            .Stop()
            .StopWords("_english_"))
        .CharFilter("my_char_filter", c => c
            .PatternReplace()
            .Pattern("[^\\w\\s]")
            .Replacement(""))
        .Normalizer("my_normalizer", n => n
            .Custom()
            .Filters("lowercase", "asciifolding"));
```

Each method takes a name (the Elasticsearch component name) and a configuration lambda.

## Analyzer types

### Custom analyzer

The most common type. Composes a tokenizer with optional char filters and token filters:

```csharp
.Analyzer("english_html", a => a
    .Custom()
    .CharFilters("html_strip")
    .Tokenizer("standard")
    .Filters("lowercase", "english_stop", "english_stemmer"))
```

:::{dropdown} Other analyzer types
**Standard analyzer (with options)**

```csharp
.Analyzer("my_standard", a => a
    .Standard()
    .MaxTokenLength(255)
    .StopWords("_english_"))
```

**Pattern analyzer**

```csharp
.Analyzer("email_parts", a => a
    .Pattern()
    .Pattern("[.@]")
    .Lowercase(true))
```
:::

## Tokenizer types

```csharp
// Edge NGram (autocomplete)
.Tokenizer("autocomplete", t => t
    .EdgeNGram()
    .MinGram(1)
    .MaxGram(20)
    .TokenChars("letter", "digit"))

// Pattern
.Tokenizer("comma_split", t => t
    .Pattern()
    .Pattern(",\\s*"))
```

:::{dropdown} More tokenizer types
```csharp
// NGram
.Tokenizer("trigram", t => t
    .NGram()
    .MinGram(3)
    .MaxGram(3)
    .TokenChars("letter", "digit"))

// Path hierarchy
.Tokenizer("file_path", t => t
    .PathHierarchy()
    .Delimiter('/'))
```
:::

## Token filter types

```csharp
// Stop words
.TokenFilter("english_stop", f => f
    .Stop()
    .StopWords("_english_"))

// Stemmer
.TokenFilter("english_stemmer", f => f
    .Stemmer()
    .Language("english"))

// Synonym graph
.TokenFilter("product_synonyms", f => f
    .SynonymGraph()
    .Synonyms("laptop, notebook, portable computer", "phone, mobile, cell"))
```

:::{dropdown} More token filter types
```csharp
// Shingle (word n-grams)
.TokenFilter("bigrams", f => f
    .Shingle()
    .MinShingleSize(2)
    .MaxShingleSize(2)
    .OutputUnigrams(false))

// Edge NGram (as filter)
.TokenFilter("autocomplete_filter", f => f
    .EdgeNGram()
    .MinGram(1)
    .MaxGram(20))
```
:::

## Character filters

```csharp
// Pattern replace
.CharFilter("strip_special", c => c
    .PatternReplace()
    .Pattern("[^a-zA-Z0-9\\s]")
    .Replacement(""))

// Mapping
.CharFilter("emoticons", c => c
    .Mapping()
    .Mappings(":) => happy", ":( => sad"))
```

## Normalizers

Normalizers apply to `keyword` fields for case-insensitive exact matching:

```csharp
.Normalizer("lowercase_normalizer", n => n
    .Custom()
    .Filters("lowercase", "asciifolding"))
```

Reference it from the attribute: `[Keyword(Normalizer = "lowercase_normalizer")]`

## Built-in analysis constants

The `BuiltInAnalysis` class provides compile-time constants for all standard Elasticsearch analysis components:

```csharp
using Elastic.Mapping.Analysis;

.Analyzer("my_analyzer", a => a
    .Custom()
    .Tokenizer(BuiltInAnalysis.Tokenizers.Standard)
    .Filters(BuiltInAnalysis.TokenFilters.Lowercase, BuiltInAnalysis.TokenFilters.AsciiFolding))
```

:::{dropdown} Available constant groups
| Class | Examples |
|-------|----------|
| `BuiltInAnalysis.Analyzers` | `Standard`, `Simple`, `Whitespace`, `Stop`, `Keyword`, `Pattern`, `Fingerprint` |
| `BuiltInAnalysis.Analyzers.Language` | `English`, `French`, `German`, `Spanish`, `Cjk` (30+ languages) |
| `BuiltInAnalysis.Tokenizers` | `Standard`, `Letter`, `Whitespace`, `NGram`, `EdgeNGram`, `PathHierarchy`, `Pattern` |
| `BuiltInAnalysis.TokenFilters` | `Lowercase`, `Uppercase`, `AsciiFolding`, `Stop`, `Stemmer`, `Snowball`, `Synonym`, `SynonymGraph`, `NGram`, `EdgeNGram`, `Shingle` (40+) |
| `BuiltInAnalysis.CharFilters` | `HtmlStrip`, `Mapping`, `PatternReplace`, `IcuNormalizer` |
| `BuiltInAnalysis.StopWords` | `English`, `French`, `German` (language-specific stop word lists) |
| `BuiltInAnalysis.StemmerLanguages` | `English`, `LightEnglish`, `Porter2`, `French`, `LightFrench` (50+ variants) |
:::

## Using strongly-typed analysis names in mappings

When you define custom analysis, the source generator emits accessor classes that provide compile-time verified names. Use these in your `ConfigureMappings` instead of raw strings:

```csharp
public class ProductConfig : IConfigureElasticsearch<Product>
{
    public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
        analysis
            .Analyzer("product_name_analyzer", a => a.Custom().Tokenizer("standard").Filters("lowercase"))
            .Normalizer("lowercase_normalizer", n => n.Custom().Filters("lowercase"));

    public MappingsBuilder<Product> ConfigureMappings(MappingsBuilder<Product> mappings) =>
        mappings
            // Use the generated accessor instead of a string literal
            .Name(f => f.Analyzer(ProductAnalysis.Analyzers.ProductNameAnalyzer))
            .Category(f => f.Normalizer(ProductAnalysis.Normalizers.LowercaseNormalizer));

    public IReadOnlyDictionary<string, string>? IndexSettings => null;
}
```

A typo in the property name is now a compile error rather than a runtime failure. See [strongly-typed analysis names](strongly-typed-analysis-names.md) for the full reference on generated accessors, base-type anchoring, and how delegation following works.

## Merging analysis from other types

Use `AnalysisBuilder.Merge` to include another type's analysis components:

```csharp
public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
    analysis
        .Analyzer("local_analyzer", a => a.Custom().Tokenizer("standard").Filters("lowercase"))
        .Merge<OtherDocument>(OtherContext.OtherDocument);
```

Names already defined on the current builder are never overwritten. See [mapping merge](merge-strategies.md) for the complete reference.

## How analysis flows to Elasticsearch

1. **Compile time**: The source generator parses your `ConfigureAnalysis` method and stores a delegate reference in the generated context.
2. **Runtime (bootstrap)**: The channel invokes the delegate, builds `AnalysisSettings`, and merges the result into the base settings JSON.
3. **Push**: The merged settings JSON (including `"analysis": { ... }`) is sent to Elasticsearch as part of the component template.
