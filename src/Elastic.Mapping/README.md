# Elastic.Mapping

Compile-time Elasticsearch mappings for .NET. **Native AOT ready.** Define your index mappings, analysis chains, and field metadata with C# attributes and get reflection-free generated code that works with `System.Text.Json` source generation out of the box.

## Why?

Elasticsearch field names are strings. Typos are silent. Refactors break queries. Manual JSON mappings drift from your code.

**Elastic.Mapping** fixes this with a source generator that turns your POCOs into type-safe, pre-computed mapping infrastructure at build time -- zero reflection, zero runtime overhead, fully AOT compatible.

## Quick Start

### 1. Define your domain types (clean POCOs)

```csharp
public class Product
{
    [Keyword]
    public string Id { get; set; }

    [Text(Analyzer = "standard")]
    public string Name { get; set; }

    public double Price { get; set; }

    public bool InStock { get; set; }

    [Nested]
    public List<Category> Categories { get; set; }
}
```

### 2. Register them in a mapping context

```csharp
[ElasticsearchMappingContext]
[Index<Product>(Name = "products")]
[DataStream<ApplicationLog>(Type = "logs", Dataset = "myapp", Namespace = "production")]
public static partial class MyContext;
```

### 3. Use generated field constants and metadata

```csharp
// Type-safe field names -- rename the C# property, these update automatically
MyContext.Product.Fields.Name      // "name"
MyContext.Product.Fields.Price     // "price"
MyContext.Product.Fields.InStock   // "inStock"

// Index targets
MyContext.Product.IndexStrategy.WriteTarget   // "products"

// Data stream naming follows Elastic conventions
MyContext.ApplicationLog.IndexStrategy.DataStreamName  // "logs-myapp-production"

// Pre-built JSON for index creation
var json = MyContext.Product.Context.GetIndexJson();

// Change detection -- only update when mappings actually change
if (clusterHash != MyContext.Product.Hash) UpdateMappings();
```

## System.Text.Json Integration

Elastic.Mapping is built around `System.Text.Json`. Link your STJ source-generated `JsonSerializerContext` and the mapping generator inherits your serialization configuration automatically -- one source of truth for both JSON serialization and Elasticsearch field names.

```csharp
// Your STJ source-generated context
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Product))]
[JsonSerializable(typeof(Order))]
public partial class MyJsonContext : JsonSerializerContext;

// Link it to the mapping context
[ElasticsearchMappingContext(JsonContext = typeof(MyJsonContext))]
[Index<Product>(Name = "products")]
[Index<Order>(Name = "orders")]
public static partial class MyContext;
```

The generator reads `[JsonSourceGenerationOptions]` at compile time and applies:

| STJ Option                 | Effect on Mappings                                                                             |
|----------------------------|------------------------------------------------------------------------------------------------|
| `PropertyNamingPolicy`     | Field names follow the same policy (`camelCase`, `snake_case_lower`, `kebab-case-lower`, etc.) |
| `UseStringEnumConverter`   | Enum fields map to `keyword` instead of `integer`                                              |
| `DefaultIgnoreCondition`   | Ignored properties are excluded from mappings                                                  |
| `IgnoreReadOnlyProperties` | Read-only properties are excluded from mappings                                                |

Per-property `[JsonPropertyName("custom_name")]` and `[JsonIgnore]` attributes are always respected, with or without a linked context.

This means your Elasticsearch field names, your JSON wire format, and your C# properties all stay in sync -- at compile time, with no reflection.

## Native AOT

Every feature in Elastic.Mapping is AOT compatible:

- **No reflection at runtime** -- all field names, mappings JSON, and type metadata are generated as constants at compile time
- **No dynamic code generation** -- source generators run during build, not at runtime
- **Pre-computed JSON** -- settings and mappings are embedded as string literals, ready to send to Elasticsearch
- **STJ source generation** -- link your `JsonSerializerContext` for a fully AOT serialization pipeline

Publish with `dotnet publish -p:PublishAot=true` and everything works.

## Field Type Attributes

Control how properties map to Elasticsearch field types:

| Attribute                   | Elasticsearch Type | Use Case                             |
|-----------------------------|--------------------|--------------------------------------|
| `[Text]`                    | `text`             | Full-text search, analyzers          |
| `[Keyword]`                 | `keyword`          | Exact match, aggregations, sorting   |
| `[Date]`                    | `date`             | Timestamps, date math                |
| `[Nested]`                  | `nested`           | Preserve array element relationships |
| `[GeoPoint]`                | `geo_point`        | Latitude/longitude                   |
| `[DenseVector(Dims = 384)]` | `dense_vector`     | Embeddings, kNN search               |
| `[SemanticText]`            | `semantic_text`    | ELSER / semantic search              |
| `[Ip]`                      | `ip`               | IPv4/IPv6 addresses                  |
| `[Completion]`              | `completion`       | Autocomplete suggestions             |

Properties without attributes are inferred from their CLR type (`string` -> `keyword`, `int` -> `integer`, `DateTime` -> `date`, etc.).

## Custom Configuration with `IConfigureElasticsearch<T>`

Implement `IConfigureElasticsearch<T>` to configure analysis, mappings, and index settings for a document type. There are two ways to wire this up:

### Option A: Dedicated configuration class

Create a separate class that implements the interface and reference it via `Configuration = typeof(...)`:

```csharp
public class ProductConfig : IConfigureElasticsearch<Product>
{
    public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis
        .Analyzer("product_search", a => a.Custom()
            .Tokenizer(BuiltIn.Tokenizers.Standard)
            .Filters(BuiltIn.TokenFilters.Lowercase, "english_stemmer", "edge_ngram_3_8"))
        .TokenFilter("english_stemmer", f => f.Stemmer()
            .Language(BuiltIn.StemmerLanguages.English))
        .TokenFilter("edge_ngram_3_8", f => f.EdgeNGram()
            .MinGram(3).MaxGram(8));

    public MappingsBuilder<Product> ConfigureMappings(MappingsBuilder<Product> mappings) => mappings
        .Name(f => f.Analyzer("product_search")
            .MultiField("keyword", mf => mf.Keyword().IgnoreAbove(256)));

    public IReadOnlyDictionary<string, string> IndexSettings => new Dictionary<string, string>
    {
        ["index.default_pipeline"] = "product-enrichment"
    };
}

[ElasticsearchMappingContext]
[Index<Product>(Name = "products", Configuration = typeof(ProductConfig))]
public static partial class MyContext;
```

### Option B: Self-configuring entity

If you prefer to keep configuration on the entity itself, implement the interface directly:

```csharp
public class Product : IConfigureElasticsearch<Product>
{
    [Keyword]
    public string Id { get; set; }

    [Text(Analyzer = "product_search")]
    public string Name { get; set; }

    public double Price { get; set; }

    public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis
        .Analyzer("product_search", a => a.Custom()
            .Tokenizer(BuiltIn.Tokenizers.Standard)
            .Filters(BuiltIn.TokenFilters.Lowercase));

    public MappingsBuilder<Product> ConfigureMappings(MappingsBuilder<Product> mappings) => mappings
        .Name(f => f.Analyzer("product_search")
            .MultiField("keyword", mf => mf.Keyword().IgnoreAbove(256)));
}

// No Configuration needed -- the generator detects the interface on the entity itself
[ElasticsearchMappingContext]
[Index<Product>(Name = "products")]
public static partial class MyContext;
```

All three interface members use default implementations, so you only need to override what you customize:

| Member               | Default    | Purpose                                            |
|----------------------|------------|----------------------------------------------------|
| `ConfigureAnalysis`  | no-op      | Custom analyzers, tokenizers, filters              |
| `ConfigureMappings`  | no-op      | Field overrides, runtime fields, dynamic templates |
| `IndexSettings`      | `null`     | Additional index settings (e.g. `default_pipeline`)|

The generated `ProductAnalysis` class gives you type-safe constants for your custom components:

```csharp
MyContext.Product.Analysis.Analyzers.ProductSearch     // "product_search"
MyContext.Product.Analysis.TokenFilters.EnglishStemmer  // "english_stemmer"
```

### Dependency Injection

If your configuration class needs services (e.g. to read settings from `IConfiguration`), call `RegisterServiceProvider` before first access to mapping JSON:

```csharp
// In your startup / Program.cs
services.AddSingleton<IConfigureElasticsearch<Product>, ProductConfig>();

var sp = services.BuildServiceProvider();
MyContext.RegisterServiceProvider(sp);
```

When no `IServiceProvider` is registered (or the service isn't found), the generator falls back to `new ProductConfig()` (parameterless constructor).

## Index Strategies

### Traditional Index

```csharp
[Index<Product>(
    Name = "products",
    WriteAlias = "products-write",
    ReadAlias = "products-read",
    Shards = 3,
    RefreshInterval = "5s"
)]
```

### Rolling Date Index

```csharp
[Index<Order>(Name = "orders", DatePattern = "yyyy.MM")]
// Write target: orders-2025.02
// Search pattern: orders-*
```

### Data Stream (logs, metrics, traces)

```csharp
[DataStream<ApplicationLog>(Type = "logs", Dataset = "ecommerce.app", Namespace = "production")]
// Data stream: logs-ecommerce.app-production
// Search pattern: logs-ecommerce.app-*
```

## Mappings Builder

The source generator creates extension methods on `MappingsBuilder<T>` for each property on your registered types. Customize mappings at the property level with full IntelliSense:

```csharp
public MappingsBuilder<Product> ConfigureMappings(MappingsBuilder<Product> mappings) => mappings
    .Name(f => f.Analyzer("product_search"))
    .Price(f => f.DocValues(true))
    .AddRuntimeField("discount_pct", r => r.Double()
        .Script("emit((doc['price'].value - doc['sale_price'].value) / doc['price'].value * 100)"))
    .AddDynamicTemplate("labels_as_keyword", dt => dt
        .PathMatch("labels.*")
        .Mapping(m => m.Keyword()));
```

## What Gets Generated

For each registered type, the source generator produces:

- **Field constants** -- `MyContext.Product.Fields.Name` (compile-time safe field names)
- **Bidirectional field mapping** -- `PropertyToField` / `FieldToProperty` dictionaries
- **Index/search strategy** -- write targets, search patterns, data stream names
- **Settings + mappings JSON** -- pre-computed, ready for index creation
- **Content hashes** -- detect when mappings change
- **Analysis accessors** -- type-safe constants for custom analyzers/filters
- **Mappings builder extensions** -- per-property fluent API on `MappingsBuilder<T>` for customization

All generated at compile time. Zero reflection at runtime. AOT compatible. Aligned with your `System.Text.Json` configuration.
