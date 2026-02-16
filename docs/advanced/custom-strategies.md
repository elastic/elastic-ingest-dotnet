---
navigation_title: Custom strategies
---

# Custom strategies

All strategy interfaces can be implemented to customize channel behavior. Before implementing a custom strategy from scratch, check whether you can achieve your goal by composing existing pieces with the factory methods.

## Composing with factory methods

The `IngestStrategies` and `BootstrapStrategies` factories cover most use cases. You can extend them by passing a custom bootstrap strategy that includes your own steps alongside the built-in ones:

```csharp
var bootstrap = new DefaultBootstrapStrategy(
    new CustomPipelineStep(),           // your custom step
    new ComponentTemplateStep(),        // built-in
    new DataStreamTemplateStep()        // built-in
);
var strategy = IngestStrategies.DataStream<LogEntry>(context, bootstrap);
var options = new IngestChannelOptions<LogEntry>(transport, strategy, context);
```

This gives you the standard data stream ingest, provisioning, and alias strategies while inserting your custom bootstrap logic.

## Implementing IBootstrapStep

Create a custom bootstrap step for any setup action:

```csharp
public class CustomPipelineStep : IBootstrapStep
{
    public string Name => "CustomPipeline";

    public async Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken ctx = default)
    {
        var body = """{ "description": "My pipeline", "processors": [] }""";
        var response = await context.Transport.RequestAsync<StringResponse>(
            HttpMethod.PUT, "_ingest/pipeline/my-pipeline",
            PostData.String(body), cancellationToken: ctx
        ).ConfigureAwait(false);

        if (response.ApiCallDetails.HasSuccessfulStatusCode) return true;

        return context.BootstrapMethod == BootstrapMethod.Silent
            ? false
            : throw new Exception($"Failed to create pipeline: {response}",
                response.ApiCallDetails.OriginalException);
    }

    public bool Execute(BootstrapContext context)
    {
        // Sync implementation follows the same pattern
        var body = """{ "description": "My pipeline", "processors": [] }""";
        var response = context.Transport.Request<StringResponse>(
            HttpMethod.PUT, "_ingest/pipeline/my-pipeline", PostData.String(body)
        );

        if (response.ApiCallDetails.HasSuccessfulStatusCode) return true;

        return context.BootstrapMethod == BootstrapMethod.Silent
            ? false
            : throw new Exception($"Failed to create pipeline: {response}",
                response.ApiCallDetails.OriginalException);
    }
}
```

## Implementing IDocumentIngestStrategy&lt;T&gt;

Custom ingest strategies control per-document bulk headers:

```csharp
public class RoutedIngestStrategy<T> : IDocumentIngestStrategy<T>
{
    private readonly Func<T, string> _routingSelector;
    private readonly string _indexName;

    public RoutedIngestStrategy(string indexName, Func<T, string> routingSelector)
    {
        _indexName = indexName;
        _routingSelector = routingSelector;
    }

    public string RefreshTargets => _indexName;

    public string GetBulkUrl(string defaultPath) => defaultPath;

    public BulkOperationHeader CreateBulkOperationHeader(T document, string channelHash) =>
        new IndexOperation { Index = _indexName, Routing = _routingSelector(document) };
}
```

## Key patterns

When implementing custom strategies:

1. **Always implement both sync and async** - all strategy interfaces have dual sync/async methods
2. **Use `ConfigureAwait(false)`** on all await calls
3. **Follow the error handling pattern** - check `BootstrapMethod.Silent` to decide between returning `false` and throwing
4. **Use `StringResponse`** for transport calls that need response body inspection
5. **Use `VoidResponse`** for transport calls where you only check the status code
