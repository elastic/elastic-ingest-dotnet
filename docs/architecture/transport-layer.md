---
navigation_title: Transport layer
---

# Transport layer

`Elastic.Ingest.Transport` bridges the buffered channel infrastructure with `Elastic.Transport` (`ITransport`). It provides the base class `TransportChannelBase` and the options base `TransportChannelOptionsBase`.

## What it does

`TransportChannelBase` passes the `ITransport` instance from options to the `ExportAsync` method, so subclasses can make HTTP calls without managing transport lifecycle.

```csharp
// Subclass implements:
protected abstract Task<TResponse> ExportAsync(
    ITransport transport,
    ArraySegment<TEvent> items,
    CancellationToken ctx
);
```

In `IngestChannelBase`, this builds a `_bulk` request from the items array and sends it through the transport.

## Transport configuration

The transport is provided through channel options:

```csharp
var transport = new DistributedTransport(
    new TransportConfiguration(new Uri("http://localhost:9200"))
);

var options = new IngestChannelOptions<MyDoc>(transport, MyContext.MyDoc.Context);
```

Transport configuration (timeouts, authentication, connection pooling) is handled by `Elastic.Transport` -- the channel inherits whatever is configured on the transport instance.

## Drain timeout

`TransportChannelBase` overrides the drain timeout to use the transport's `RequestTimeout`. This means drain calculations account for how long each bulk request might take, rather than using an arbitrary default.

## Direct transport calls

Strategies and orchestrators use the transport directly for non-bulk operations (template creation, alias management, rollover). Common patterns:

```csharp
// PUT with body
await transport.PutAsync<StringResponse>(url, PostData.String(body), cancellationToken: ctx);

// HEAD check (exists?)
var head = await transport.HeadAsync(url, ctx);
if (head.ApiCallDetails.HttpStatusCode == 200) { /* exists */ }

// GET with response
var response = await transport.GetAsync<StringResponse>(url, ctx);
```

See [custom strategies](../advanced/custom-strategies.md) for examples of using the transport in your own strategy implementations.
