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

# `IndexChannel<TEvent>`

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
