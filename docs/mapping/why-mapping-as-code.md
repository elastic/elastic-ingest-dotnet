---
navigation_title: Why mapping as code
---

# Why mapping as code

Elasticsearch mappings define how your data is indexed, searched, and stored. Traditionally, teams manage these mappings as JSON files, Kibana dev tools scripts, or Terraform resources that live separately from the application code that produces the data. This separation creates real problems.

## The drift problem

When your mapping lives in a JSON file or an ops pipeline and your document model lives in C#, they inevitably drift apart:

- A developer adds a new property to the C# class but forgets to update the mapping JSON.
- Someone renames a field in the mapping but the application still writes the old name.
- Two teams change the same index template without coordinating.
- A typo in an analyzer name only surfaces when documents fail to index in production.

None of these failures surface at compile time. You find them in logs, in missing search results, or in production incidents.

## Mapping as code

`Elastic.Mapping` takes a different approach: your C# document class **is** the mapping. Attributes on properties declare the Elasticsearch field types. A source generator produces the mapping JSON at compile time. If the class compiles, the mapping is valid.

```csharp
public class Product
{
    [Id]
    [Keyword]
    public string Sku { get; set; }

    [Text(Analyzer = "product_name_analyzer")]
    public string Name { get; set; }

    [Keyword(Normalizer = "lowercase")]
    public string Category { get; set; }

    [Double]
    public decimal Price { get; set; }
}
```

This gives you:

- **One source of truth.** The C# class defines both your application's data model and its Elasticsearch mapping. They cannot drift.
- **Compile-time verification.** A renamed property updates the mapping automatically. A deleted field disappears from the template. A misspelled analyzer name is a build error (when using strongly-typed accessors).
- **Refactoring safety.** IDE rename refactorings propagate through your mapping. You do not need to remember to update a separate JSON file.
- **Code review.** Mapping changes show up in pull requests alongside the application logic that motivated them.

## What about custom analysis?

Field attributes cover the mapping shape, but real-world search requires custom analyzers, tokenizers, and filters. These are also defined in code through `IConfigureElasticsearch<T>`:

```csharp
public class ProductConfig : IConfigureElasticsearch<Product>
{
    public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
        analysis
            .Analyzer("product_name_analyzer", a => a
                .Custom()
                .Tokenizer("standard")
                .Filters("lowercase", "asciifolding"))
            .Normalizer("lowercase", n => n
                .Custom()
                .Filters("lowercase"));

    public MappingsBuilder<Product> ConfigureMappings(MappingsBuilder<Product> mappings) => mappings;
    public IReadOnlyDictionary<string, string>? IndexSettings => null;
}
```

The source generator discovers the analysis component names and emits strongly-typed accessor classes. Referencing `ProductAnalysis.Analyzers.ProductNameAnalyzer` instead of the string `"product_name_analyzer"` turns a runtime failure into a compile error.

## Comparison with other approaches

| Approach | Drift risk | Compile-time safety | Deploys with app |
|----------|-----------|--------------------|--------------------|
| JSON files in ops repo | High | None | No |
| Terraform / Pulumi | Medium | Schema validation only | Separate pipeline |
| Kibana dev tools | High | None | No |
| **Elastic.Mapping** | **None** | **Full** | **Yes** |

## When not to use this

Mapping-as-code works best when your application owns the data it writes to Elasticsearch. If you are:

- Ingesting data from external sources with unpredictable schemas, consider dynamic templates instead of exhaustive attribute-based mapping.
- Managing shared indices that multiple teams write to, you may still want a coordination layer on top.
- Using Elasticsearch purely for log storage with Elastic Agent / Fleet, the built-in index templates handle mapping for you.
