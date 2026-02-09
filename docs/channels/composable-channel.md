---
navigation_title: Composable channel
---

# ElasticsearchChannel&lt;T&gt;

`ElasticsearchChannel<T>` is the primary channel type. It uses a composable strategy pattern where each aspect of channel behavior is delegated to a pluggable strategy.

## Auto-configuration

When you provide an `ElasticsearchTypeContext` (from `Elastic.Mapping`), strategies are automatically resolved:

```csharp
var options = new ElasticsearchChannelOptions<LogEntry>(transport, MyContext.LogEntry);
var channel = new ElasticsearchChannel<LogEntry>(options);
```

The channel automatically selects:
- **Ingest strategy**: based on `EntityTarget` (DataStream, Index, WiredStream)
- **Bootstrap strategy**: component templates + appropriate index template
- **Provisioning strategy**: hash-based reuse when content hashing is available
- **Alias strategy**: latest + search aliases when configured in the type context

## Manual configuration

Override any strategy for full control:

```csharp
var options = new ElasticsearchChannelOptions<MyDoc>(transport)
{
    TemplateName = "my-template",
    TemplateWildcard = "my-template-*",
    IngestStrategy = new IndexIngestStrategy<MyDoc>("my-index"),
    BootstrapStrategy = new DefaultBootstrapStrategy(
        new ComponentTemplateStep(),
        new IndexTemplateStep()
    ),
    ProvisioningStrategy = new AlwaysCreateProvisioning(),
    AliasStrategy = new NoAliasStrategy()
};
```

## Available options

| Option | Description |
|--------|-------------|
| `TypeContext` | Source-generated type context for auto-configuration |
| `IngestStrategy` | Controls per-document bulk operation headers and URLs |
| `BootstrapStrategy` | Controls template and index creation |
| `ProvisioningStrategy` | Controls whether to create or reuse indices |
| `AliasStrategy` | Controls alias management after indexing |
| `RolloverStrategy` | Controls manual index/data stream rollover |
| `DataStreamLifecycleRetention` | Data retention period for data stream lifecycle |
| `IlmPolicy` | ILM policy name for the settings component template |

## Rollover

When a `RolloverStrategy` is configured, you can trigger manual rollover:

```csharp
options.RolloverStrategy = new ManualRolloverStrategy();
var channel = new ElasticsearchChannel<LogEntry>(options);

// Rollover with conditions
await channel.RolloverAsync(maxAge: "7d", maxSize: "50gb");

// Unconditional rollover
await channel.RolloverAsync();
```
