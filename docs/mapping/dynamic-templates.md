---
navigation_title: Dynamic templates
---

# Dynamic templates

Dynamic templates define catch-all rules for fields not explicitly declared on your document type. When Elasticsearch encounters a field name matching the template's pattern, it applies the specified mapping instead of the default dynamic behavior.

## Adding dynamic templates

Use `AddDynamicTemplate` on `MappingsBuilder<T>` in your `ConfigureMappings` method:

```csharp
public MappingsBuilder<LogEntry> ConfigureMappings(MappingsBuilder<LogEntry> mappings) =>
    mappings
        .AddDynamicTemplate("strings_as_keywords", t => t
            .MatchMappingType("string")
            .Mapping(f => f.Keyword()))
        .AddDynamicTemplate("labels_as_keywords", t => t
            .PathMatch("labels.*")
            .Mapping(f => f.Keyword()));
```

## DynamicTemplateBuilder API

| Method | Description |
|--------|-------------|
| `Match(string)` | Matches field names using a simple pattern (supports `*` wildcard) |
| `Unmatch(string)` | Excludes field names matching this pattern |
| `PathMatch(string)` | Matches full dotted field paths (e.g. `labels.*`) |
| `PathUnmatch(string)` | Excludes dotted field paths matching this pattern |
| `MatchMappingType(string)` | Matches fields by detected JSON type (`string`, `long`, `double`, `boolean`, `date`, `object`) |
| `MatchPattern(string)` | Sets the pattern style (`regex` or `simple`, default is `simple`) |
| `Mapping(Func<FieldBuilder, FieldBuilder>)` | Defines the mapping applied to matched fields |

## Common patterns

### Map all unmapped strings as keywords

Prevents Elasticsearch from creating both `text` and `keyword` sub-fields for every dynamic string:

```csharp
.AddDynamicTemplate("strings_as_keywords", t => t
    .MatchMappingType("string")
    .Mapping(f => f.Keyword()))
```

### Map label fields as keywords

When your documents have a dynamic `labels` object:

```csharp
.AddDynamicTemplate("labels", t => t
    .PathMatch("labels.*")
    .Mapping(f => f.Keyword()))
```

### Use regex pattern matching

```csharp
.AddDynamicTemplate("ip_fields", t => t
    .MatchPattern("regex")
    .Match(".*_ip$")
    .Mapping(f => f.Ip()))
```

## Template ordering

Dynamic templates are evaluated in order. The first matching template wins. Place more specific templates before general ones:

```csharp
mappings
    .AddDynamicTemplate("ip_fields", t => t       // Specific: IP fields
        .Match("*_ip")
        .Mapping(f => f.Ip()))
    .AddDynamicTemplate("date_fields", t => t     // Specific: date fields
        .Match("*_at")
        .Mapping(f => f.Date()))
    .AddDynamicTemplate("all_strings", t => t     // General: everything else
        .MatchMappingType("string")
        .Mapping(f => f.Keyword()));
```

## Interaction with field attributes

Dynamic templates apply only to fields not already mapped. Fields declared with attributes on your document type always take priority. This makes dynamic templates useful for:

- Open-ended metadata objects (e.g. `labels`, `tags`, `metrics`)
- Documents with a known core schema plus variable extension fields
- Catch-all defaults that reduce index bloat from unexpected fields

## Complete example

```csharp
public class LogEntry : IConfigureElasticsearch<LogEntry>
{
    [Timestamp]
    public DateTimeOffset Timestamp { get; set; }

    [Text]
    public string Message { get; set; }

    [Keyword]
    public string Level { get; set; }

    // Dynamic object, handled by template below
    public Dictionary<string, string> Labels { get; set; }

    public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis;

    public MappingsBuilder<LogEntry> ConfigureMappings(MappingsBuilder<LogEntry> mappings) =>
        mappings
            .AddDynamicTemplate("labels_as_keywords", t => t
                .PathMatch("labels.*")
                .Mapping(f => f.Keyword().IgnoreAbove(256)));

    public IReadOnlyDictionary<string, string>? IndexSettings => null;
}
```
