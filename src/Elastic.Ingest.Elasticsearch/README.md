# Elastic.Ingest.Elasticsearch

`Elastic.Channels` implementations of `BufferedChannelBase` that allows data to pushed to either indices or data streams


## `DataStreamChannel<TEvent>`

A channel that specializes to writing data with a timestamp to Elasticsearch data streams. E.g given the following document. 

```csharp
public class TimeSeriesDocument
{
    [JsonPropertyName("@timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }
}

```

A channel can be created to push data to the `logs-dotnet-default` data stream.

```csharp
var dataStream = new DataStreamName("logs", "dotnet");
var bufferOptions = new BufferOptions { }
var options = new DataStreamChannelOptions<TimeSeriesDocument>(transport)
{
  DataStream = dataStream,
  BufferOptions = bufferOptions
};
var channel = new DataStreamChannel<TimeSeriesDocument>(options);
```

NOTE: read more about Elastic's data stream naming convention here:
https://www.elastic.co/blog/an-introduction-to-the-elastic-data-stream-naming-scheme

we can now push data to Elasticsearch using the `DataStreamChannel`
```csharp
var doc = new TimeSeriesDocument 
{ 
    Timestamp = DateTimeOffset.Now, 
    Message = "Hello World!", 
}
channel.TryWrite(doc);
```

### Bootstrap target data stream

Optionally the target data stream can be bootstrapped using the following.

```csharp
await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure, "7-days-default"); 
```

This will try and set up the target data stream with the `7-days-default` ILM policy.
Throwing exceptions if it fails to do so because `BootstrapMethod.Failure` was provided

An index template with accompanying component templates will be created based on the type and dataset portion
of the target datastream.

## `IndexChannel<TEvent>`

A channel that specializes in writing catalog data to Elastic indices. 
Catalog data is typically data that has an id of sorts.

Given the following minimal document

```csharp
public class CatalogDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("created")]
    public DateTimeOffset Created { get; set; }
}
```

We can create an `IndexChannel<>` to push `CatalogDocument` instances.

```csharp
var options = new IndexChannelOptions<CatalogDocument>(transport)
{
    IndexFormat = "catalog-data-{0:yyyy.MM.dd}",
    BulkOperationIdLookup = c => c.Id,
    TimestampLookup = c => c.Created,
};
var channel = new IndexChannel<CatalogDocument>(options);
```

now we can push data using:

```csharp
var doc = new CatalogDocument 
{ 
    Created = date, 
    Title = "Hello World!", 
    Id = "hello-world" 
}
channel.TryWrite(doc);
```

This will push data to `catalog-data-2023.01.1` because `TimestampLookup` yields `Created` to `IndexFormat`.

`IndexFormat` can also simply be a fixed string to write to an Elasticsearch alias/index.

`BulkOperationIdLookup` determines if the document should be pushed to Elasticsearch using a `create` or `index` operation.

### Bootstrap target index 

Optionally the target index can be bootstrapped using the following.

```csharp
await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure, "7-days-default"); 
```

This will try and set up the target data stream with the `7-days-default` ILM policy.
Throwing exceptions if it fails to do so because `BootstrapMethod.Failure` was provided

An index template with accompanying component templates will be created based named using `IndexFormat`.

## `CatalogChannel<TDocument>` (experimental)

Where `IndexChannel<>` and `DataStreamChannel<>` are build for `time-series` use-cases `CatalogChannel<TDocument>` is build 
for cases where we expect to write all data to a single index during the limited lifetime of the application.

It inherits `IndexChannel<>` and therefor equally ensures index and component templates get registered.

```csharp
var options = new CatalogIndexChannelOptions<MyDocument>(transport)
{
  SerializerContext = ExampleJsonSerializerContext.Default,
  GetMapping = () => // language=json
    $$"""
    {
      "properties": {
        "message": {
          "type": "text"
        }
      }
    }
    """
};
var c = new CatalogIndexChannel<MyDocument>(options);
await c.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
````
This will by default write all documents to `mydocument-{DateTimeOffset.UtcNow:yyyy.MM.dd.HHmmss}` where the `DateTimeOffset.UtcNow`
gets queried only once during the channel's instantiation.

You can wait for documents to be indexed and refresh elasticsearch at your expected termination point.

```csharp
await c.WaitForDrainAsync();
await c.RefreshAsync();
```

After which you can use you apply the following aliases:

* `mydocument-latest` the last written index
* `mydocument` the active search alias

```csharp
await c.ApplyAliasesAsync();
```

You can also do this separately:

```csharp
await c.ApplyLatestAliasAsync();
// Call this later to swap search alias over over to the latest index
await c.ApplyActiveSearchAliasAsync();
```

## `SemanticIndexChannel<TDocument>` (experimental)

A specialization of `CatalogIndexChannel<TDocument>` 

```csharp
var options = new SemanticIndexChannelOptions<MyDocument>(transport)
{
    BufferOptions = bufferOptions,
    CancellationToken = cancellationTokenSource.Token,
    SerializerContext = ExampleJsonSerializerContext.Default,
    InferenceCreateTimeout = TimeSpan.FromMinutes(5),
    GetMapping = (inferenceId, searchInferenceId) => // language=json
      $$"""
      {
        "properties": {
          "message": {
              "type": "text",
              "fields": {
                  "semantic": {
                      "type": "semantic_text",
                      "inference_id": "{{inferenceId}}"
                  }
              }
          }
        }
      }
      """
};
  
var c = new SemanticIndexChannel<MyDocument>(options);
```
The bootstrapping of which by default ensures that inference endpoints using default Elastic-built LLM providers are registered. 

However, external inference identifiers can be provided as well.

```csharp
var options = new SemanticIndexChannelOptions<MyDocument>(transport)
{
   UsePreexistingInferenceIds = true   
   InferenceId = "my-inference-id",
   SearchInferenceId = "my-search-inference-id"
};
```


