---
navigation_title: Rollover
---

# Rollover strategies

Rollover strategies control manual rollover of indices or data streams. Rollover creates a new backing index when conditions are met.

## IRolloverStrategy

```csharp
public interface IRolloverStrategy
{
    Task<bool> RolloverAsync(RolloverContext context, CancellationToken ctx = default);
    bool Rollover(RolloverContext context);
}
```

## Built-in strategies

### NoRolloverStrategy

No-op. Always returns `true` without performing any rollover.

### ManualRolloverStrategy

Calls `POST {target}/_rollover` with optional conditions:

```csharp
var options = new IngestChannelOptions<LogEntry>(transport, MyContext.LogEntry)
{
    RolloverStrategy = new ManualRolloverStrategy()
};
var channel = new IngestChannel<LogEntry>(options);

// Rollover with conditions
await channel.RolloverAsync(maxAge: "7d", maxSize: "50gb");

// Rollover with max document count
await channel.RolloverAsync(maxDocs: 10_000_000);

// Unconditional rollover
await channel.RolloverAsync();
```

## Rollover conditions

| Condition | Description |
|-----------|-------------|
| `maxAge` | Maximum age of the index (e.g. "7d", "30d") |
| `maxSize` | Maximum primary shard size (e.g. "50gb") |
| `maxDocs` | Maximum number of documents |

When no conditions are specified, rollover is unconditional.

## When to use

Manual rollover is useful when:
- ILM is not available (e.g. self-managed clusters without ILM)
- You need programmatic control over when rollover happens
- You want to trigger rollover based on application-level events
