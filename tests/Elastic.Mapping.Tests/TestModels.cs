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
[Index<LogEntry>(
	WriteAlias = "logs-write",
	ReadAlias = "logs-read",
	Shards = 3,
	Replicas = 2
)]
[DataStream<NginxAccessLog>(
	Type = "logs",
	Dataset = "nginx.access",
	Namespace = "production"
)]
[Index<SimpleDocument>(Name = "simple-docs")]
[Index<AdvancedDocument>(Name = "advanced-docs")]
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
// EXTENDED MAPPING CONTEXT: tests attribute options not covered above
// ============================================================================

[ElasticsearchMappingContext]
[Index<RollingIndex>(
	Name = "rolling",
	WriteAlias = "rolling-write",
	DatePattern = "yyyy.MM",
	RefreshInterval = "5s",
	Dynamic = false
)]
[Index<GeoDocument>(Name = "geo-docs")]
[Index<SimpleDocument>(Name = "simple-semantic", Variant = "Semantic")]
public static partial class ExtendedTestMappingContext;

// ============================================================================
// TEMPLATED MAPPING CONTEXT: tests NameTemplate + CreateContext generation
// ============================================================================

[ElasticsearchMappingContext]
[Index<KnowledgeArticle>(
	NameTemplate = "docs-{searchType}-{env}",
	DatePattern = "yyyy.MM.dd.HHmmss"
)]
[Index<KnowledgeArticle>(
	NameTemplate = "articles-{team}-{component}",
	Variant = "Multi"
)]
[Index<LocationRecord>(
	NameTemplate = "geo-{namespace}"
)]
public static partial class TemplatedMappingContext;

/// <summary>Test model for templated index with custom + well-known placeholders.</summary>
public class KnowledgeArticle
{
	[Id]
	public string ArticleId { get; set; } = string.Empty;

	[Timestamp]
	public DateTimeOffset PublishedAt { get; set; }

	[Text]
	public string Title { get; set; } = string.Empty;
}

/// <summary>Test model for templated index with only a well-known namespace placeholder.</summary>
public class LocationRecord
{
	[Id]
	public string RecordId { get; set; } = string.Empty;

	public double Latitude { get; set; }
	public double Longitude { get; set; }
}

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
[Index<SelfConfiguredDocument>(Name = "self-configured")]
[Index<ExternallyConfiguredDocument>(Name = "ext-configured", Configuration = typeof(ExternalDocumentConfig))]
[Index<PartiallyConfiguredDocument>(Name = "partial-configured", Configuration = typeof(PartialDocumentConfig))]
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

	/// <inheritdoc />
	public IReadOnlyDictionary<string, string>? IndexSettings => null;
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
	/// <inheritdoc />
	public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis;

	public MappingsBuilder<PartiallyConfiguredDocument> ConfigureMappings(MappingsBuilder<PartiallyConfiguredDocument> mappings) =>
		mappings.Title(f => f.Analyzer("standard").MultiField("keyword", mf => mf.Keyword().IgnoreAbove(256)));

	/// <inheritdoc />
	public IReadOnlyDictionary<string, string>? IndexSettings => null;
}

// ============================================================================
// DI CONTEXT: isolated context for testing RegisterServiceProvider
// ============================================================================

[ElasticsearchMappingContext]
[Index<DiTestDocument>(Name = "di-test", Configuration = typeof(DiTestDocumentConfig))]
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

	/// <inheritdoc />
	public MappingsBuilder<DiTestDocument> ConfigureMappings(MappingsBuilder<DiTestDocument> mappings) => mappings;

	/// <inheritdoc />
	public IReadOnlyDictionary<string, string>? IndexSettings => null;
}

// ============================================================================
// AI ENRICHMENT CONTEXT: tests [AiEnrichment<T>] + [AiInput] + [AiField]
// ============================================================================

/// <summary>
/// Document type with AI enrichment attributes.
/// </summary>
public class DocumentationPage
{
	[Id]
	[Keyword]
	public string Url { get; set; } = string.Empty;

	[AiInput]
	[Text]
	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	[AiInput]
	[Text]
	[JsonPropertyName("body")]
	public string Body { get; set; } = string.Empty;

	[AiField("A concise two-sentence summary of the document content.")]
	[Text]
	[JsonPropertyName("ai_summary")]
	public string? AiSummary { get; set; }

	[AiField("3 to 5 questions this document answers, phrased as a user would ask.", MinItems = 3, MaxItems = 5)]
	[Keyword]
	[JsonPropertyName("ai_questions")]
	public string[]? AiQuestions { get; set; }
}

[ElasticsearchMappingContext]
[Index<DocumentationPage>(
	Name = "docs-pages",
	WriteAlias = "docs-pages-write",
	ReadAlias = "docs-pages-read"
)]
[AiEnrichment<DocumentationPage>(
	Role = "You are a documentation analysis assistant.",
	MatchField = "url"
)]
public static partial class AiTestMappingContext;

// ============================================================================
// AI ENRICHMENT CONTEXT WITH INDEX VARIANT: tests IndexVariant targeting
// ============================================================================

public class VariantDocumentationPage
{
	[Id]
	[Keyword]
	public string Url { get; set; } = string.Empty;

	[AiInput]
	[Text]
	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	[AiInput]
	[Text]
	[JsonPropertyName("body")]
	public string Body { get; set; } = string.Empty;

	[AiField("A concise two-sentence summary of the document content.")]
	[Text]
	[JsonPropertyName("ai_summary")]
	public string? AiSummary { get; set; }

	[AiField("3 to 5 questions this document answers, phrased as a user would ask.", MinItems = 3, MaxItems = 5)]
	[Keyword]
	[JsonPropertyName("ai_questions")]
	public string[]? AiQuestions { get; set; }
}

[ElasticsearchMappingContext]
[Index<VariantDocumentationPage>(
	Name = "docs-primary",
	WriteAlias = "docs-primary",
	ReadAlias = "docs-primary-read",
	DatePattern = "yyyy.MM.dd.HHmmss"
)]
[Index<VariantDocumentationPage>(
	Name = "docs-secondary",
	Variant = "Secondary",
	WriteAlias = "docs-secondary",
	ReadAlias = "docs-secondary-read",
	DatePattern = "yyyy.MM.dd.HHmmss"
)]
[AiEnrichment<VariantDocumentationPage>(
	Role = "You are a documentation analysis assistant.",
	MatchField = "url",
	IndexVariant = "Secondary"
)]
public static partial class AiVariantTestMappingContext;

// ============================================================================
// STJ CONTEXT: tests JsonContext integration with naming policies
// ============================================================================

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(SnakeCaseDocument))]
public partial class TestJsonContext : JsonSerializerContext;

[ElasticsearchMappingContext(JsonContext = typeof(TestJsonContext))]
[Index<SnakeCaseDocument>(Name = "snake-docs")]
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

// ============================================================================
// MAPPING BUG-FIX CONTEXT: covers Bugs 1-3 and Gaps 1-2
// ============================================================================

/// <summary>Sub-type whose attribute-declared mappings should be emitted within [Object] parents.</summary>
public class IndexedProduct
{
	[Keyword(Normalizer = "keyword_normalizer")]
	public string Id { get; set; } = string.Empty;

	[Keyword(Normalizer = "keyword_normalizer")]
	public string Repository { get; set; } = string.Empty;

	[Text]
	public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Document exercising Bug 1 (string[] with [Text]), Bug 2 ([Object] sub-type),
/// and Bug 3 (attribute-only fields with no builder call).
/// </summary>
public class MappingBugDocument : IConfigureElasticsearch<MappingBugDocument>
{
	[Id]
	[Keyword]
	public string Url { get; set; } = string.Empty;

	[Object]
	public IndexedProduct? Product { get; set; }

	[Text]
	[JsonPropertyName("ai_questions")]
	public string[]? AiQuestions { get; set; }

	[Text]
	[JsonPropertyName("ai_short_summary")]
	public string? AiShortSummary { get; set; }

	[Keyword]
	[JsonPropertyName("ai_search_query")]
	public string? AiSearchQuery { get; set; }

	[Text]
	[JsonPropertyName("ai_rag_summary")]
	public string? AiRagSummary { get; set; }

	public string[]? Tags { get; set; }

	public List<int>? Scores { get; set; }

	public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis;

	public MappingsBuilder<MappingBugDocument> ConfigureMappings(MappingsBuilder<MappingBugDocument> mappings) =>
		mappings
			.AiRagSummary(f => f.Analyzer("standard"))
			.AddField("ai_questions.semantic", f => f.SemanticText().InferenceId("my-elser"));

	public IReadOnlyDictionary<string, string>? IndexSettings => null;
}

[ElasticsearchMappingContext]
[Index<MappingBugDocument>(Name = "mapping-bug-test")]
public static partial class MappingBugMappingContext;

// ============================================================================
// GAP-FIX CONTEXT: covers remaining gaps — conditional [JsonIgnore] + dot-path
// merge, and [Object] array traversal
// ============================================================================

/// <summary>
/// Document exercising Gap 1 ([Text] + [JsonIgnore(WhenWritingNull)] + dot-path AddField)
/// and Gap 2 ([Object] array with sub-type attributes).
/// </summary>
public class GapFixDocument : IConfigureElasticsearch<GapFixDocument>
{
	[Id]
	[Keyword]
	public string Url { get; set; } = string.Empty;

	[Text]
	[JsonPropertyName("ai_questions")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string[]? AiQuestions { get; set; }

	[Text]
	[JsonPropertyName("ai_use_cases")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string[]? AiUseCases { get; set; }

	[Object]
	[JsonPropertyName("product")]
	public IndexedProduct? Product { get; set; }

	[Object]
	[JsonPropertyName("related_products")]
	public IndexedProduct[]? RelatedProducts { get; set; }

	[JsonIgnore]
	public string InternalOnly { get; set; } = string.Empty;

	public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis;

	public MappingsBuilder<GapFixDocument> ConfigureMappings(MappingsBuilder<GapFixDocument> mappings) =>
		mappings
			.AddField("ai_questions.semantic_text", f => f.SemanticText().InferenceId("my-elser"))
			.AddField("ai_questions.jina", f => f.SemanticText().InferenceId("my-jina"))
			.AddField("ai_use_cases.semantic_text", f => f.SemanticText().InferenceId("my-elser"));

	public IReadOnlyDictionary<string, string>? IndexSettings => null;
}

[ElasticsearchMappingContext]
[Index<GapFixDocument>(Name = "gap-fix-test")]
public static partial class GapFixMappingContext;

// ============================================================================
// CLR TYPE INFERENCE CONTEXT: no Elastic.Mapping field attributes, all types inferred
// ============================================================================

/// <summary>
/// Covers every CLR → Elasticsearch type mapping from <c>TypeAnalyzer.InferFieldType</c>.
/// No [Text], [Keyword], [Date], etc. attributes — only plain CLR types.
/// </summary>
public class ClrInferenceDocument
{
	// string → text
	public string StringField { get; set; } = string.Empty;
	public string? NullableStringField { get; set; }
	public string[] StringArrayField { get; set; } = [];
	public List<string> StringListField { get; set; } = [];

	// numerics
	public int IntField { get; set; }
	public int? NullableIntField { get; set; }
	public long LongField { get; set; }
	public long? NullableLongField { get; set; }
	public short ShortField { get; set; }
	public short? NullableShortField { get; set; }
	public byte ByteField { get; set; }
	public byte? NullableByteField { get; set; }
	public double DoubleField { get; set; }
	public double? NullableDoubleField { get; set; }
	public float FloatField { get; set; }
	public float? NullableFloatField { get; set; }
	public decimal DecimalField { get; set; }   // decimal → double
	public decimal? NullableDecimalField { get; set; }

	// bool → boolean
	public bool BoolField { get; set; }
	public bool? NullableBoolField { get; set; }

	// dates → date
	public DateTime DateTimeField { get; set; }
	public DateTime? NullableDateTimeField { get; set; }
	public DateTimeOffset DateTimeOffsetField { get; set; }
	public DateTimeOffset? NullableDateTimeOffsetField { get; set; }

	// Guid → keyword
	public Guid GuidField { get; set; }
	public Guid? NullableGuidField { get; set; }

	// enum → keyword
	public ClrInferenceStatus StatusField { get; set; }
	public ClrInferenceStatus? NullableStatusField { get; set; }

	// nested object → object
	public ClrInferenceAddress? AddressField { get; set; }
	public List<ClrInferenceAddress> AddressListField { get; set; } = [];
}

public enum ClrInferenceStatus { Active, Inactive }

public class ClrInferenceAddress
{
	public string Street { get; set; } = string.Empty;
	public string City { get; set; } = string.Empty;
}

[ElasticsearchMappingContext]
[Index<ClrInferenceDocument>(Name = "clr-inference-test")]
public static partial class ClrInferenceMappingContext;

// ============================================================================
// EXPLICIT CONTAINER TEST MODEL
// Exercises the new AddField (multi-field) / AddProperty (sub-property) API.
// ============================================================================

/// <summary>
/// Document with an explicit [Text] field for AddField and an [Object] field for AddProperty,
/// demonstrating that the explicit container intent is honoured through the generated pipeline.
/// </summary>
public class ExplicitContainerDocument : IConfigureElasticsearch<ExplicitContainerDocument>
{
	[Id]
	[Keyword]
	public string Id { get; set; } = string.Empty;

	[Text]
	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	[Object]
	[JsonPropertyName("meta")]
	public IndexedProduct? Meta { get; set; }

	public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis;

	public MappingsBuilder<ExplicitContainerDocument> ConfigureMappings(MappingsBuilder<ExplicitContainerDocument> mappings) =>
		mappings
			// title is [Text] (leaf) → AddField puts the child under fields
			.AddField("title.semantic", f => f.SemanticText().InferenceId("my-elser"))
			// meta is [Object] → AddProperty puts the child under properties
			.AddProperty("meta.extra", f => f.Keyword());

	public IReadOnlyDictionary<string, string>? IndexSettings => null;
}

[ElasticsearchMappingContext]
[Index<ExplicitContainerDocument>(Name = "explicit-container-test")]
public static partial class ExplicitContainerMappingContext;

// ============================================================================
// INHERITANCE TEST MODELS
// Covers base-type property inclusion and generic-constrained extension methods.
// ============================================================================

/// <summary>
/// Root base — never directly registered; only appears as an ancestor.
/// Its properties must appear in every derived document's mapping JSON,
/// Fields accessor, and hash.
/// </summary>
public class InheritanceBase
{
	[Id]
	[Keyword]
	[JsonPropertyName("id")]
	public string Id { get; set; } = string.Empty;

	[Text]
	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	[Keyword]
	[JsonPropertyName("status")]
	public string Status { get; set; } = string.Empty;
}

/// <summary>
/// Mid-level type: directly registered in <see cref="InheritanceMappingContext"/>
/// AND the base of <see cref="DerivedPage"/>.
/// Exercises the partial-class dedup: both a closed extension class
/// (<c>IntermediatePageMappingsExtensions</c>) and a generic-constrained base
/// extension class (<c>IntermediatePageMappingsExtensions</c>, partial) must
/// be emitted without CS0101.
/// </summary>
public class IntermediatePage : InheritanceBase
{
	[Keyword]
	[JsonPropertyName("section")]
	public string Section { get; set; } = string.Empty;
}

/// <summary>
/// Leaf type: inherits from <see cref="IntermediatePage"/> which inherits from
/// <see cref="InheritanceBase"/>. Exercises 3-level inheritance — DerivedPage's
/// mapping must include title/status (from InheritanceBase), section (from
/// IntermediatePage), and content (own).
/// </summary>
public class DerivedPage : IntermediatePage
{
	[Text]
	[JsonPropertyName("content")]
	public string Content { get; set; } = string.Empty;
}

/// <summary>
/// Configuration for <see cref="DerivedPage"/> that uses generic-constrained
/// helper methods. These helpers compile ONLY if the generator emitted correct
/// generic-constrained extension methods for InheritanceBase and IntermediatePage.
/// </summary>
public class DerivedPageConfig : IConfigureElasticsearch<DerivedPage>
{
	// Generic helper constrained to InheritanceBase — exercises Title<TDoc>, Status<TDoc>
	private static MappingsBuilder<T> AddBaseOverrides<T>(MappingsBuilder<T> m) where T : InheritanceBase =>
		m.Title(f => f.Analyzer("standard"));

	// Generic helper constrained to IntermediatePage — exercises Section<TDoc>
	private static MappingsBuilder<T> AddIntermediateOverrides<T>(MappingsBuilder<T> m) where T : IntermediatePage =>
		m.Section(f => f.IgnoreAbove(256));

	public MappingsBuilder<DerivedPage> ConfigureMappings(MappingsBuilder<DerivedPage> mappings) =>
		AddBaseOverrides(AddIntermediateOverrides(mappings));

	public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) => analysis;
	public IReadOnlyDictionary<string, string>? IndexSettings => null;
}

[ElasticsearchMappingContext]
[Index<IntermediatePage>(Name = "intermediate-docs")]
[Index<DerivedPage>(Name = "derived-docs", Configuration = typeof(DerivedPageConfig))]
public static partial class InheritanceMappingContext;

// ============================================================================
// DELEGATION TEST MODELS: analysis defined in a shared factory, referenced
// via ConfigureAnalysis delegation — mirrors the website-ai-search pattern.
// ============================================================================

/// <summary>
/// Shared analysis factory that lives in a separate class from the mapping context,
/// mirroring the SharedAnalysisFactory pattern in website-ai-search.
/// </summary>
public static class SearchAnalysisFactory
{
	// Const reference — should resolve via semantic const-resolution in the parser.
	public const string KeywordNormalizerName = "keyword_normalizer";

	/// <summary>
	/// Base analysis — names discoverable via delegation following.
	/// </summary>
	public static AnalysisBuilder BuildBaseAnalysis(AnalysisBuilder analysis) => analysis
		.Normalizer(KeywordNormalizerName, n => n.Custom()
			.Filters("lowercase", "asciifolding"))
		.Analyzer("starts_with_analyzer", a => a.Custom()
			.Tokenizer("starts_with_tokenizer")
			.Filter("lowercase"))
		.Analyzer("starts_with_analyzer_search", a => a.Custom()
			.Tokenizer("keyword")
			.Filter("lowercase"))
		.CharFilter("strip_non_word", cf => cf.PatternReplace()
			.Pattern(@"\W")
			.Replacement(" "))
		.TokenFilter("english_stop", tf => tf.Stop()
			.Stopwords("_english_"))
		.Tokenizer("starts_with_tokenizer", t => t.EdgeNGram()
			.MinGram(1)
			.MaxGram(10)
			.TokenChars("letter", "digit"));

	/// <summary>
	/// Extended analysis — calls BuildBaseAnalysis transitively, verifying deep delegation.
	/// </summary>
	public static AnalysisBuilder BuildExtendedAnalysis(AnalysisBuilder analysis, string[] synonyms) =>
		BuildBaseAnalysis(analysis)
			.Analyzer("synonyms_analyzer", a => a.Custom()
				.Tokenizer("standard")
				.Filters("lowercase", "synonyms_filter"))
			.TokenFilter("synonyms_filter", tf => tf.SynonymGraph()
				.Synonyms(synonyms));
}

/// <summary>
/// Base document type for delegation tests. The generator should anchor the analysis
/// accessor to this type since all derived docs share SearchAnalysisFactory.
/// </summary>
public class SearchBaseDocument
{
	[Id]
	[Keyword]
	public string Id { get; set; } = string.Empty;

	[Text]
	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;
}

/// <summary>First derived doc type — delegates to SearchAnalysisFactory.BuildBaseAnalysis.</summary>
public class SearchArticle : SearchBaseDocument
{
	[Text]
	[JsonPropertyName("body")]
	public string Body { get; set; } = string.Empty;
}

/// <summary>Second derived doc type — delegates transitively (BuildExtendedAnalysis → BuildBaseAnalysis).</summary>
public class SearchProduct : SearchBaseDocument
{
	[Keyword]
	[JsonPropertyName("sku")]
	public string Sku { get; set; } = string.Empty;
}

public class SearchArticleConfig : IConfigureElasticsearch<SearchArticle>
{
	// Delegates to shared factory — the parser must follow this call to find the names.
	public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		SearchAnalysisFactory.BuildBaseAnalysis(analysis);

	public MappingsBuilder<SearchArticle> ConfigureMappings(MappingsBuilder<SearchArticle> mappings) => mappings;
	public IReadOnlyDictionary<string, string>? IndexSettings => null;
}

public class SearchProductConfig : IConfigureElasticsearch<SearchProduct>
{
	// Delegates transitively: BuildExtendedAnalysis → BuildBaseAnalysis.
	public AnalysisBuilder ConfigureAnalysis(AnalysisBuilder analysis) =>
		SearchAnalysisFactory.BuildExtendedAnalysis(analysis, ["elastic => search, find"]);

	public MappingsBuilder<SearchProduct> ConfigureMappings(MappingsBuilder<SearchProduct> mappings) => mappings;
	public IReadOnlyDictionary<string, string>? IndexSettings => null;
}

/// <summary>
/// Compile-time reachability test for generated analysis accessor surfaces.
/// This class compiles ONLY if the generator correctly emits:
/// (1) SearchBaseDocumentAnalysis static accessor, and
/// (2) generic-constrained extension methods on MappingsBuilder&lt;TDoc&gt; where TDoc : SearchBaseDocument.
/// A build failure here means the generator did not produce the expected surfaces.
/// </summary>
public static class SearchMappingHelpers
{
	// Surface (1): static accessor — reachable anywhere without generic context.
	public static string KeywordNormalizerKey => SearchBaseDocumentAnalysis.Normalizers.KeywordNormalizer;
	public static string StartsWithAnalyzerKey => SearchBaseDocumentAnalysis.Analyzers.StartsWithAnalyzer;
	public static string EnglishStopKey => SearchBaseDocumentAnalysis.TokenFilters.EnglishStop;

	// Surface (2): generic-constrained extensions on MappingsBuilder<TDoc>.
	// These compile only if the extensions are emitted with the correct `where TDoc : SearchBaseDocument` constraint.
	public static MappingsBuilder<T> UseAnalysisKeys<T>(MappingsBuilder<T> m) where T : SearchBaseDocument
	{
		// Both surfaces reachable from the same generic method — this is the key consumer pattern.
		var normalizerName = SearchBaseDocumentAnalysis.Normalizers.KeywordNormalizer;  // surface (1)
		var sameViaExtension = m.Normalizers().KeywordNormalizer;                       // surface (2)
		_ = normalizerName == sameViaExtension; // same value — both return "keyword_normalizer"
		return m;
	}
}

[ElasticsearchMappingContext]
[Index<SearchArticle>(Name = "search-articles", Configuration = typeof(SearchArticleConfig))]
[Index<SearchProduct>(Name = "search-products", Configuration = typeof(SearchProductConfig))]
public static partial class DelegationTestMappingContext;

// ============================================================================
// SHARED NESTED TYPE TEST MODELS: regression for nested-builder dedup racing
// across the base-type extension emission path and the own-property emission
// path when both reference the same nested record type by name.
// ============================================================================

/// <summary>
/// Nested type referenced from both a base-type property and an unrelated top-level property.
/// <see cref="Value"/> has no explicit [JsonIgnore], so under a context whose JsonContext sets
/// <c>DefaultIgnoreCondition = Always</c> it is analyzed as ignored — a *different* member shape
/// for the exact same CLR type than a context with no such default. <see cref="Key"/> pins itself
/// to always-included via an explicit Condition=Never so both contexts agree on it.
/// </summary>
public class SharedNestedMeta
{
	[JsonIgnore(Condition = JsonIgnoreCondition.Never)]
	[Keyword]
	public string Key { get; set; } = string.Empty;

	[Text]
	public string Value { get; set; } = string.Empty;
}

/// <summary>Base type declaring the [Object] property — emitted via EmitBaseExtensionsClass for each derived type.</summary>
public abstract class SharedNestedBase
{
	[JsonIgnore(Condition = JsonIgnoreCondition.Never)]
	[Id]
	[Keyword]
	public string Id { get; set; } = string.Empty;

	[JsonIgnore(Condition = JsonIgnoreCondition.Never)]
	[Object]
	public SharedNestedMeta? Meta { get; set; }
}

/// <summary>
/// First subclass, registered under a context whose JsonContext narrows the analyzed shape of
/// <see cref="SharedNestedMeta"/> (DefaultIgnoreCondition = Always drops <c>Value</c>).
/// </summary>
public class SharedNestedDocA : SharedNestedBase
{
	[JsonIgnore(Condition = JsonIgnoreCondition.Never)]
	[Text]
	public string TitleA { get; set; } = string.Empty;
}

/// <summary>Second subclass, registered under a context with the default (unrestricted) shape.</summary>
public class SharedNestedDocB : SharedNestedBase
{
	[Text]
	public string TitleB { get; set; } = string.Empty;
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.Always)]
[JsonSerializable(typeof(SharedNestedDocA))]
public partial class SharedNestedJsonContextA : JsonSerializerContext;

[ElasticsearchMappingContext(JsonContext = typeof(SharedNestedJsonContextA))]
[Index<SharedNestedDocA>(Name = "shared-nested-a")]
public static partial class SharedNestedContextA;

[ElasticsearchMappingContext]
[Index<SharedNestedDocB>(Name = "shared-nested-b")]
public static partial class SharedNestedContextB;

/// <summary>
/// Unrelated top-level type that independently declares its own property of the same
/// nested record type — exercises the EmitForContext (own-property) emission path for
/// a nested type name that is also emitted via EmitBaseExtensionsClass above.
/// </summary>
public class IndependentNestedDoc
{
	[Id]
	public string Id { get; set; } = string.Empty;

	[Object]
	public SharedNestedMeta? OtherMeta { get; set; }
}

[ElasticsearchMappingContext]
[Index<IndependentNestedDoc>(Name = "independent-nested")]
public static partial class IndependentNestedMappingContext;

/// <summary>
/// Compile-time reachability test for the generated <c>SharedNestedMetaNestedBuilder</c>.
/// This compiles ONLY if the winner of the nested-builder dedup race exposes BOTH <c>Key</c>
/// and <c>Value</c> — regardless of whether that winner was SharedNestedContextA's narrowed
/// analysis (which alone would drop <c>Value</c>) or another registration's full analysis.
/// A build failure here (CS1061: 'SharedNestedMetaNestedBuilder' does not contain a definition
/// for 'Value') means the generator regressed to emitting whichever shape won first.
/// </summary>
public static class SharedNestedMappingHelpers
{
	public static MappingsBuilder<T> ConfigureSharedMeta<T>(MappingsBuilder<T> m) where T : SharedNestedBase =>
		m.Meta((SharedNestedMetaNestedBuilder meta) => meta.Key(k => k).Value(v => v));

	public static MappingsBuilder<IndependentNestedDoc> ConfigureOtherMeta(MappingsBuilder<IndependentNestedDoc> m) =>
		m.OtherMeta((SharedNestedMetaNestedBuilder meta) => meta.Key(k => k).Value(v => v));
}
