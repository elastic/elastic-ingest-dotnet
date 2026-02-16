---
navigation_title: Provisioning
---

# Provisioning strategies

Provisioning strategies control whether to create a new index or reuse an existing one when the channel starts.

## IIndexProvisioningStrategy

```csharp
public interface IIndexProvisioningStrategy
{
    Task<string> ProvisionAsync(ProvisioningContext context, CancellationToken ctx = default);
    string Provision(ProvisioningContext context);
}
```

## Built-in strategies

### AlwaysCreateProvisioning

Always creates a new index. This is the default when no content hashing is available.

### HashBasedReuseProvisioning

Compares the content hash of the channel's mappings/settings with existing indices. If a matching index exists, it reuses it instead of creating a new one. This avoids creating duplicate indices when the schema hasn't changed.

Auto-selected when `ElasticsearchTypeContext.GetContentHash` is available.
