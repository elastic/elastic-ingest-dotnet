---
navigation_title: Semantic channel
---

# Semantic channel

A semantic channel combines document ingestion with ELSER inference endpoint creation. This is typically set up using `ElasticsearchChannel<T>` with an `InferenceEndpointStep` in the bootstrap pipeline.

## Usage

```csharp
var options = new ElasticsearchChannelOptions<Article>(transport, MyContext.Article);
options.BootstrapStrategy = new DefaultBootstrapStrategy(
    new InferenceEndpointStep("my-elser-endpoint", numThreads: 2),
    new ComponentTemplateStep(),
    new DataStreamTemplateStep()
);

var channel = new ElasticsearchChannel<Article>(options);
await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
```

## InferenceEndpointStep

The `InferenceEndpointStep` creates an ELSER sparse embedding inference endpoint:

- **inferenceId**: the inference endpoint name to create
- **numThreads**: number of threads for the model (default: 1)
- **usePreexisting**: if `true`, uses an existing endpoint instead of creating one
- **createTimeout**: timeout for endpoint creation (default: transport default)

The step checks if the endpoint exists before creating, making it idempotent.

## When to use

Use a semantic channel when:
- Your documents need semantic/vector search capabilities
- You want ELSER inference endpoints created as part of bootstrap
- You need the inference endpoint to exist before indexing begins
