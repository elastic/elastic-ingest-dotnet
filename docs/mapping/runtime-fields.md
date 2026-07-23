---
navigation_title: Runtime fields
---

# Runtime fields

Runtime fields are computed at query time using Painless scripts. They are not indexed and exist only in the mapping definition. Use them for derived values that do not need to be searchable at scale or that change logic without requiring a reindex.

## Adding runtime fields

Use `AddRuntimeField` on `MappingsBuilder<T>` in your `ConfigureMappings` method:

```csharp
public MappingsBuilder<Order> ConfigureMappings(MappingsBuilder<Order> mappings) =>
    mappings
        .AddRuntimeField("day_of_week", r => r
            .Keyword()
            .Script("emit(doc['@timestamp'].value.dayOfWeekEnum.getDisplayName(TextStyle.FULL, Locale.ROOT))"))
        .AddRuntimeField("total_with_tax", r => r
            .Double()
            .Script("emit(doc['subtotal'].value * 1.21)"));
```

## RuntimeFieldBuilder API

The builder requires two steps: choose the type, then provide the script.

### Type methods

| Method | ES type | Description |
|--------|---------|-------------|
| `Keyword()` | `keyword` | String values (exact match, aggregations) |
| `Long()` | `long` | Integer values |
| `Double()` | `double` | Floating-point values |
| `Date()` | `date` | Date/time values |
| `Boolean()` | `boolean` | True/false values |
| `Ip()` | `ip` | IP addresses |
| `GeoPoint()` | `geo_point` | Latitude/longitude pairs |

### Script method

| Method | Description |
|--------|-------------|
| `Script(string)` | Painless script that calls `emit(value)` to produce the field value |

Both type and script are required. The builder throws at build time if either is missing.

## Examples

```csharp
// Day of week from timestamp
.AddRuntimeField("day_of_week", r => r
    .Keyword()
    .Script("emit(doc['@timestamp'].value.dayOfWeekEnum.getDisplayName(TextStyle.FULL, Locale.ROOT))"))

// Computed numeric value
.AddRuntimeField("price_per_unit", r => r
    .Double()
    .Script("emit(doc['total_price'].value / doc['quantity'].value)"))

// Boolean flag from condition
.AddRuntimeField("is_completed", r => r
    .Boolean()
    .Script("emit(doc['status.keyword'].value == 'completed')"))

// Full name from parts
.AddRuntimeField("full_name", r => r
    .Keyword()
    .Script("""
        def first = doc['first_name.keyword'].value;
        def last = doc['last_name.keyword'].value;
        emit(first + ' ' + last)
        """))
```

## When to use runtime fields

**Good for:**

- Derived values that are cheap to compute (e.g. `day_of_week`)
- Exploration before committing to indexing a field
- Schema evolution without reindexing
- Exposing computed values without exposing source fields

**Avoid for:**

- High-cardinality aggregations (per-document script cost adds up)
- Full-text search (runtime fields cannot use analyzers)
- Sort-heavy workloads (sorting evaluates the script for every matching document)

## Generated output

Runtime fields defined in `ConfigureMappings` are included in the component template JSON under the `runtime` section:

```json
{
  "properties": { "..." },
  "runtime": {
    "day_of_week": {
      "type": "keyword",
      "script": { "source": "emit(...)" }
    }
  }
}
```
