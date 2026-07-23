---
navigation_title: Field attributes
---

# Field attributes

Field attributes on your document class properties control which Elasticsearch field type the source generator emits. When no attribute is present, the generator infers a type from CLR conventions.

## Default type inference

| CLR type | Default ES field type |
|----------|----------------------|
| `string` | `text` with `.keyword` sub-field |
| `int`, `long` | `long` |
| `float`, `double`, `decimal` | `double` |
| `bool` | `boolean` |
| `DateTime`, `DateTimeOffset` | `date` |
| `Guid` | `keyword` |
| Enum types | `keyword` (serialized by name) |
| Complex types (classes) | `object` (recursively mapped) |
| `IEnumerable<T>` | Array of inferred type for `T` |

Use explicit attributes to override the default or set field-specific options.

## Text and keyword fields

```csharp
public class Article
{
    [Text(Analyzer = "english", SearchAnalyzer = "english")]
    public string Body { get; set; }

    [Keyword(Normalizer = "lowercase", IgnoreAbove = 256)]
    public string Category { get; set; }

    [Completion(Analyzer = "simple")]
    public string Suggest { get; set; }
}
```

:::{dropdown} [Text] options
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Analyzer` | `string?` | `null` | Analyzer used at index time |
| `SearchAnalyzer` | `string?` | `null` | Analyzer used at search time (falls back to `Analyzer`) |
| `Norms` | `bool` | `true` | Store field length norms for relevance scoring |
| `Index` | `bool` | `true` | Whether the field is searchable |

When no `[Text]` or `[Keyword]` attribute is present on a `string` property, the generator defaults to `text` with an automatic `.keyword` sub-field. Use `[Text]` explicitly when you need to set analyzer options.
:::

:::{dropdown} [Keyword] options
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Normalizer` | `string?` | `null` | Normalizer for case-insensitive matching |
| `IgnoreAbove` | `int` | `0` (no limit) | Values longer than this are not indexed (still stored) |
| `DocValues` | `bool` | `true` | Store doc values for sorting and aggregations |
| `Index` | `bool` | `true` | Whether the field is searchable |
:::

:::{dropdown} [Completion] options
| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Analyzer` | `string?` | `null` | Analyzer for indexing |
| `SearchAnalyzer` | `string?` | `null` | Analyzer for search |
:::

## Numeric, date, and boolean fields

```csharp
public class Order
{
    [Long]
    public int Quantity { get; set; }

    [Double]
    public decimal Price { get; set; }

    [Date(Format = "strict_date_optional_time||epoch_millis")]
    public DateTimeOffset CreatedAt { get; set; }

    [Boolean]
    public bool IsActive { get; set; }
}
```

:::{dropdown} Options for numeric and date fields
All of `[Long]`, `[Double]`, `[Date]`, and `[Boolean]` support:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `DocValues` | `bool` | `true` | Store doc values for sorting and aggregations |
| `Index` | `bool` | `true` | Whether the field is searchable |

`[Date]` additionally supports:

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Format` | `string?` | `null` | Date format string |
:::

## Geo and network fields

```csharp
public class Store
{
    [GeoPoint]
    public GeoLocation Location { get; set; }

    [GeoShape]
    public object DeliveryZone { get; set; }

    [Ip]
    public string ClientIp { get; set; }
}
```

These attributes have no additional options.

## Vector and semantic fields

```csharp
public class Document
{
    [DenseVector(Dims = 384, Similarity = "cosine")]
    public float[] Embedding { get; set; }

    [SemanticText(InferenceId = "my-elser-endpoint")]
    public string Content { get; set; }
}
```

:::{dropdown} [DenseVector] and [SemanticText] options
**[DenseVector]**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Dims` | `int` | `0` | Number of dimensions |
| `Similarity` | `string?` | `null` | Similarity function (`cosine`, `dot_product`, `l2_norm`) |

**[SemanticText]**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `InferenceId` | `string?` | `null` | Inference endpoint ID |
:::

## Complex types

```csharp
public class Order
{
    [Object]
    public Address ShippingAddress { get; set; }

    [Nested(IncludeInParent = true)]
    public List<LineItem> Items { get; set; }
}
```

:::{dropdown} [Object] and [Nested] options
**[Object]**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | `bool` | `true` | When `false`, the object is stored but not indexed |

**[Nested]**

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `IncludeInParent` | `bool` | `false` | Include nested documents in the parent document |
| `IncludeInRoot` | `bool` | `false` | Include nested documents in the root document |

Use `[Nested]` when you need to query array elements independently (e.g. "find orders where item.name = X AND item.price < Y" without cross-matching).
:::

## Ingest metadata attributes

:::{note}
These attributes do not produce Elasticsearch field type mappings. They tell the source generator to produce accessor delegates that the ingest channel uses at runtime for bulk operations, index naming, and deduplication.
:::

| Attribute | Purpose | Effect on ingest |
|-----------|---------|-----------------|
| `[Id]` | Marks the document `_id` field | Bulk operations use `index` (upsert) instead of `create` |
| `[Timestamp]` | Marks the timestamp field | Required for data streams; used for date-based index naming |
| `[ContentHash]` | Marks a content hash field | Enables hash-based index reuse (skip recreating unchanged indices) |
| `[BatchIndexDate]` | Auto-stamped batch date | All documents in a batch share one timestamp for index naming |
| `[LastUpdated]` | Auto-stamped current time | Each document gets the write time |

```csharp
public class Product
{
    [Id]
    [Keyword]
    public string Sku { get; set; }

    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }

    [ContentHash]
    [Keyword]
    public string Hash { get; set; }

    [BatchIndexDate]
    public DateTimeOffset IndexedAt { get; set; }

    [LastUpdated]
    public DateTimeOffset UpdatedAt { get; set; }
}
```

## AI enrichment attributes

:::{note}
These attributes mark fields for the AI enrichment pipeline. They do not affect Elasticsearch field type mappings.
:::

| Attribute | Purpose |
|-----------|---------|
| `[AiInput]` | Field content is sent to the LLM as input |
| `[AiField]` | Field is populated by LLM output |

## TSDB dimensions

Use `[Dimension]` on keyword fields when `DataStreamMode = DataStreamMode.Tsdb`:

```csharp
public class Metric
{
    [Dimension]
    [Keyword]
    public string Host { get; set; }

    [Dimension]
    [Keyword]
    public string Service { get; set; }

    [Long]
    public long CpuPercent { get; set; }
}
```

## Combining attributes

Field attributes can be combined with ingest metadata attributes on the same property:

```csharp
public class Product
{
    [Id]           // Ingest: use as _id
    [Keyword]      // Mapping: keyword field type
    public string Sku { get; set; }

    [Timestamp]    // Ingest: use for date-pattern naming
    [Date]         // Mapping: date field type
    public DateTimeOffset CreatedAt { get; set; }
}
```
