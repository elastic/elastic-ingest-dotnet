---
navigation_title: Serialization
---

# Serialization

Elastic.Ingest.Elasticsearch uses `System.Text.Json` for all serialization. It supports custom serialization contexts for AOT scenarios and custom event writers for full control over document serialization.

## Default behavior

By default, documents are serialized using `System.Text.Json.JsonSerializer` with these options:

- `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault` (omits properties with default values)
- `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping` (fast encoding, safe for Elasticsearch)

The channel serializes documents into NDJSON format for the `_bulk` API: each document becomes a pair of lines (operation header + document body).

## JsonSerializerContext for AOT

For Native AOT or trimming scenarios, provide a `JsonSerializerContext` to enable source-generated serialization:

```csharp
[JsonSerializable(typeof(Product))]
public partial class MySerializerContext : JsonSerializerContext { }
```

Set it on the channel options:

```csharp
var options = new IngestChannelOptions<Product>(transport, MyContext.Product.Context)
{
    SerializerContext = MySerializerContext.Default
};
```

For multiple document types, use `SerializerContexts`:

```csharp
options.SerializerContexts = new JsonSerializerContext[]
{
    ProductSerializerContext.Default,
    OrderSerializerContext.Default
};
```

The library combines your contexts with its internal contexts (`IngestSerializationContext` for bulk operations, `ElasticsearchTransportSerializerContext` for transport types) using `JsonTypeInfoResolver.Combine`.

## IElasticsearchEventWriter

For full control over how documents are serialized, implement `IElasticsearchEventWriter<TEvent>`:

```csharp
public class CustomWriter : IElasticsearchEventWriter<Product>
{
    // Stream-based writing (all .NET versions)
    public Func<Stream, Product, CancellationToken, Task>? WriteToStreamAsync { get; set; }
        = async (stream, product, ctx) =>
        {
            // Custom serialization logic
            await JsonSerializer.SerializeAsync(stream, product, ctx);
        };

    // Memory-based writing (.NET Standard 2.1+ / .NET 8.0+)
    public Action<ArrayBufferWriter<byte>, Product>? WriteToArrayBuffer { get; set; }
        = (buffer, product) =>
        {
            // Custom serialization logic using Utf8JsonWriter
            using var writer = new Utf8JsonWriter(buffer);
            // ... write JSON
        };
}
```

Set it on the channel options:

```csharp
var options = new IngestChannelOptions<Product>(transport, MyContext.Product.Context)
{
    EventWriter = new CustomWriter()
};
```

When an event writer is set, the channel uses it instead of `JsonSerializer` for the document body. The bulk operation header is still serialized by the library.

## Serialization pipeline

The full serialization flow for each document in a bulk request:

1. **Operation header**: serialized by the library (for example, `{"index":{"_index":"products","_id":"ABC"}}`)
2. **Newline**: `\n`
3. **Document body**: serialized by your event writer (if set) or `JsonSerializer`
4. **Newline**: `\n`

For update operations, the document body is wrapped in `{"doc_as_upsert":true,"doc":...}` automatically.
