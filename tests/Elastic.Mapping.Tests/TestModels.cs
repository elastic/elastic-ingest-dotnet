// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Analysis;
using Elastic.Mapping.Mappings;

namespace Elastic.Mapping.Tests;

// ============================================================================
// MAPPING CONTEXT: registers all test types
// ============================================================================

[ElasticsearchMappingContext]
[Entity<LogEntry>(
	Target = EntityTarget.Index,
	WriteAlias = "logs-write",
	ReadAlias = "logs-read",
	SearchPattern = "logs-*",
	Shards = 3,
	Replicas = 2
)]
[Entity<NginxAccessLog>(
	Target = EntityTarget.DataStream,
	Type = "logs",
	Dataset = "nginx.access",
	Namespace = "production"
)]
[Entity<SimpleDocument>(Target = EntityTarget.Index, Name = "simple-docs")]
[Entity<AdvancedDocument>(Target = EntityTarget.Index, Name = "advanced-docs")]
public static partial class TestMappingContext
{
	/// <summary>Configures LogEntry-specific analysis settings (context-level).</summary>
	public static AnalysisBuilder ConfigureLogEntryAnalysis(AnalysisBuilder analysis) => analysis
		.Analyzer("log_message_analyzer", a => a
			.Custom()
			.Tokenizer(BuiltInAnalysis.Tokenizers.Standard)
			.Filters(BuiltInAnalysis.TokenFilters.Lowercase))
		.Normalizer("lowercase", n => n
			.Custom()
			.Filters(BuiltInAnalysis.TokenFilters.Lowercase));
}

// ============================================================================
// DOMAIN TYPES: clean POCOs, no mapping attributes on the class itself
// ============================================================================

/// <summary>
/// Test model with Index configuration (registered via context).
/// </summary>
public class LogEntry
{
	[JsonPropertyName("@timestamp")]
	[Timestamp]
	public DateTime Timestamp { get; set; }

	[JsonPropertyName("log.level")]
	[Keyword(Normalizer = "lowercase")]
	public string Level { get; set; } = string.Empty;

	[Text(Analyzer = "standard", Norms = false)]
	public string Message { get; set; } = string.Empty;

	public int StatusCode { get; set; }

	public double Duration { get; set; }

	public bool IsError { get; set; }

	[Ip]
	public string? ClientIp { get; set; }

	[JsonIgnore]
	public string InternalId { get; set; } = string.Empty;
}

/// <summary>
/// Test model with DataStream configuration (registered via context).
/// </summary>
public class NginxAccessLog
{
	[JsonPropertyName("@timestamp")]
	[Date(Format = "strict_date_optional_time")]
	[Timestamp]
	public DateTime Timestamp { get; set; }

	[Text(Analyzer = "standard")]
	public string Path { get; set; } = string.Empty;

	public int StatusCode { get; set; }

	[Ip]
	public string? ClientIp { get; set; }
}

/// <summary>
/// Test model with minimal configuration.
/// </summary>
public class SimpleDocument
{
	[Id]
	public string Name { get; set; } = string.Empty;
	public int Value { get; set; }
	public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Test model with advanced field types.
/// </summary>
public class AdvancedDocument
{
	[Id]
	public string Title { get; set; } = string.Empty;

	[GeoPoint]
	public object? Location { get; set; }

	[DenseVector(Dims = 384, Similarity = "cosine")]
	public float[]? Embedding { get; set; }

	[SemanticText(InferenceId = "my-elser-endpoint")]
	public string? SemanticContent { get; set; }

	[Completion(Analyzer = "simple")]
	public string? Suggest { get; set; }

	[Nested]
	public List<Tag>? Tags { get; set; }

	[ContentHash]
	public string? Hash { get; set; }
}

/// <summary>
/// Nested type for testing.
/// </summary>
public class Tag
{
	public string Name { get; set; } = string.Empty;
	public string Value { get; set; } = string.Empty;
}

// ============================================================================
// EXTENDED MAPPING CONTEXT: tests Entity attribute options not covered above
// ============================================================================

[ElasticsearchMappingContext]
[Entity<RollingIndex>(
	Target = EntityTarget.Index,
	Name = "rolling",
	WriteAlias = "rolling-write",
	SearchPattern = "rolling-*",
	DatePattern = "yyyy.MM",
	RefreshInterval = "5s",
	Dynamic = false
)]
[Entity<GeoDocument>(Target = EntityTarget.Index, Name = "geo-docs")]
[Entity<SimpleDocument>(Target = EntityTarget.Index, Name = "simple-semantic", Variant = "Semantic")]
public static partial class ExtendedTestMappingContext;

/// <summary>
/// Test model for rolling index with date pattern, refresh interval, and dynamic=false.
/// </summary>
public class RollingIndex
{
	[Id]
	public string Name { get; set; } = string.Empty;

	[Timestamp]
	public DateTimeOffset EventTime { get; set; }

	public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Test model with explicit field type attributes that override CLR inference,
/// plus [GeoShape] and [Object] field types.
/// </summary>
public class GeoDocument
{
	[Id]
	public string Name { get; set; } = string.Empty;

	[GeoShape]
	public object? Boundary { get; set; }

	[Object]
	public object? Metadata { get; set; }

	[Long]
	public int Count { get; set; }

	[Double]
	public int Score { get; set; }

	[Boolean]
	public string? Active { get; set; }
}

// ============================================================================
// INTERFACE CONFIGURATION CONTEXT: tests IConfigureElasticsearch<T>
// ============================================================================

[ElasticsearchMappingContext]
[Entity<SelfConfiguredDocument>(Target = EntityTarget.Index, Name = "self-configured")]
[Entity<ExternallyConfiguredDocument>(Target = EntityTarget.Index, Name = "ext-configured", Configuration = typeof(ExternalDocumentConfig))]
[Entity<PartiallyConfiguredDocument>(Target = EntityTarget.Index, Name = "partial-configured", Configuration = typeof(PartialDocumentConfig))]
public static partial class InterfaceTestMappingContext;

/// <summary>
/// Entity that implements IConfigureElasticsearch on itself (no separate Configuration class).
/// </summary>
public class SelfConfiguredDocument : IConfigureElasticsearch<SelfConfiguredDocument>
{
	[Id]
	[Keyword]
	public string Name { get; set; } = string.Empty;

	[Text(Analyzer = "self_analyzer")]
	public string Body { get; set; } = string.Empty;

	public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		analysis.Analyzer("self_analyzer", a => a.Custom()
			.Tokenizer(BuiltInAnalysis.Tokenizers.Standard)
			.Filters(BuiltInAnalysis.TokenFilters.Lowercase));

	public MappingsBuilder<SelfConfiguredDocument> ConfigureMappings(MappingsBuilder<SelfConfiguredDocument> mappings) =>
		mappings.AddRuntimeField("name_length", f => f.Long().Script("emit(doc['name'].value.length())"));
}

/// <summary>
/// Entity configured via a dedicated Configuration class implementing the interface.
/// </summary>
public class ExternallyConfiguredDocument
{
	[Id]
	[Keyword]
	public string Code { get; set; } = string.Empty;

	[Text(Analyzer = "ext_analyzer")]
	public string Content { get; set; } = string.Empty;

	[Double]
	public double Score { get; set; }
}

public class ExternalDocumentConfig : IConfigureElasticsearch<ExternallyConfiguredDocument>
{
	public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		analysis.Analyzer("ext_analyzer", a => a.Custom()
			.Tokenizer(BuiltInAnalysis.Tokenizers.Standard)
			.Filters(BuiltInAnalysis.TokenFilters.Lowercase, BuiltInAnalysis.TokenFilters.AsciiFolding));

	public MappingsBuilder<ExternallyConfiguredDocument> ConfigureMappings(MappingsBuilder<ExternallyConfiguredDocument> mappings) =>
		mappings.AddRuntimeField("score_tier", f => f.Keyword()
			.Script("if (doc['score'].value < 5) emit('low'); else emit('high')"));

	public IReadOnlyDictionary<string, string> IndexSettings => new Dictionary<string, string>
	{
		["index.default_pipeline"] = "ext-pipeline"
	};
}

/// <summary>
/// Configuration that only overrides ConfigureMappings, relying on defaults for the rest.
/// </summary>
public class PartiallyConfiguredDocument
{
	[Id]
	[Keyword]
	public string Id { get; set; } = string.Empty;

	[Text]
	public string Title { get; set; } = string.Empty;
}

public class PartialDocumentConfig : IConfigureElasticsearch<PartiallyConfiguredDocument>
{
	public MappingsBuilder<PartiallyConfiguredDocument> ConfigureMappings(MappingsBuilder<PartiallyConfiguredDocument> mappings) =>
		mappings.Title(f => f.Analyzer("standard").MultiField("keyword", mf => mf.Keyword().IgnoreAbove(256)));
}

// ============================================================================
// DI CONTEXT: isolated context for testing RegisterServiceProvider
// ============================================================================

[ElasticsearchMappingContext]
[Entity<DiTestDocument>(Target = EntityTarget.Index, Name = "di-test", Configuration = typeof(DiTestDocumentConfig))]
public static partial class DiTestMappingContext;

public class DiTestDocument
{
	[Id]
	[Keyword]
	public string Id { get; set; } = string.Empty;

	[Text(Analyzer = "di_analyzer")]
	public string Content { get; set; } = string.Empty;
}

public class DiTestDocumentConfig : IConfigureElasticsearch<DiTestDocument>
{
	public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		analysis.Analyzer("di_analyzer", a => a.Custom()
			.Tokenizer(BuiltInAnalysis.Tokenizers.Standard)
			.Filters(BuiltInAnalysis.TokenFilters.Lowercase));
}

// ============================================================================
// STJ CONTEXT: tests JsonContext integration with naming policies
// ============================================================================

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(SnakeCaseDocument))]
public partial class TestJsonContext : JsonSerializerContext;

[ElasticsearchMappingContext(JsonContext = typeof(TestJsonContext))]
[Entity<SnakeCaseDocument>(Target = EntityTarget.Index, Name = "snake-docs")]
public static partial class StjTestMappingContext;

/// <summary>
/// Test model for STJ snake_case naming policy.
/// Properties should be mapped to snake_case field names.
/// </summary>
public class SnakeCaseDocument
{
	public string FirstName { get; set; } = string.Empty;
	public string LastName { get; set; } = string.Empty;
	public int PageCount { get; set; }
	public DateTime CreatedAt { get; set; }
}
