---
navigation_title: Ingest
---

# Ingest strategies

Ingest strategies control how documents are written to Elasticsearch via the bulk API. They determine the bulk operation header for each document and the target URL.

## IDocumentIngestStrategy&lt;T&gt;

```csharp
public interface IDocumentIngestStrategy<in TDocument>
{
    BulkOperationHeader CreateBulkOperationHeader(TDocument document, string channelHash);
    string GetBulkUrl(string defaultPath);
    string RefreshTargets { get; }
}
```

## Built-in strategies

### DataStreamIngestStrategy

Writes `create` operations to a data stream:

```json
{"create": {"_index": "logs-myapp-default"}}
```

### IndexIngestStrategy

Writes `index` operations to a named index:

```json
{"index": {"_index": "my-index"}}
```

### TypeContextIndexIngestStrategy

Like `IndexIngestStrategy` but resolves the index name, document ID, and routing from `ElasticsearchTypeContext` accessors. Supports date-based index naming and hash-based index rotation.

### CatalogIngestStrategy

Writes `index` operations with explicit document IDs for upsert patterns:

```json
{"index": {"_index": "products", "_id": "product-123"}}
```

### WiredStreamIngestStrategy

Writes to wired streams (Elasticsearch serverless managed streams). Uses `create` operations without explicit index targeting.
