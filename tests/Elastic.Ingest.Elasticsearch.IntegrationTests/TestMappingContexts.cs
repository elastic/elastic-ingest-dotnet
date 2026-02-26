// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

/// <summary>
/// Central mapping context for all integration test scenarios.
/// Each entity gets a unique prefix to isolate Elasticsearch resources per test group.
/// Analysis and mapping customization is externalized via <c>Configuration = typeof(...)</c>.
///
/// V2 variants (same Elasticsearch targets, different analysis/runtime-fields) exist to test
/// mapping evolution: bootstrapping with V2 after V1 produces a different template hash, so
/// the channel detects the change and updates the templates.
/// </summary>
[ElasticsearchMappingContext]

// ── Data stream: logs-srvmetrics-default ────────────────────────────────
[DataStream<ServerMetricsEvent>(
	Type = "logs",
	Dataset = "srvmetrics",
	Namespace = "default",
	Configuration = typeof(ServerMetricsEventConfig))]
[DataStream<ServerMetricsEventV2>(
	Type = "logs",
	Dataset = "srvmetrics",
	Namespace = "default",
	Configuration = typeof(ServerMetricsEventV2Config))]

// ── Single index: idx-products ──────────────────────────────────────────
[Index<ProductCatalog>(
	Name = "idx-products",
	RefreshInterval = "5s",
	Configuration = typeof(ProductCatalogConfig))]
[Index<ProductCatalogV2>(
	Name = "idx-products",
	RefreshInterval = "5s",
	Configuration = typeof(ProductCatalogV2Config))]

// ── Manual alias: cat-products ──────────────────────────────────────────
[Index<ProductCatalog>(
	Name = "cat-products",
	Variant = "Catalog",
	WriteAlias = "cat-products",
	ReadAlias = "cat-products-search",
	DatePattern = "yyyy.MM.dd.HHmmss",
	Configuration = typeof(ProductCatalogConfig))]
[Index<ProductCatalogV2>(
	Name = "cat-products",
	Variant = "Catalog",
	WriteAlias = "cat-products",
	ReadAlias = "cat-products-search",
	DatePattern = "yyyy.MM.dd.HHmmss",
	Configuration = typeof(ProductCatalogV2Config))]

// ── Scripted hash upserts: hashable-articles ────────────────────────────
[Index<HashableArticle>(
	Name = "hashable-articles",
	Configuration = typeof(HashableArticleConfig))]

// ── Orchestrator primary/secondary pair for HashableArticle ─────────────
[Index<HashableArticle>(
	Name = "orch-primary",
	Variant = "Primary",
	WriteAlias = "orch-primary",
	ReadAlias = "orch-primary-search",
	DatePattern = "yyyy.MM.dd.HHmmss",
	Configuration = typeof(HashableArticleConfig))]
[Index<HashableArticle>(
	Name = "orch-secondary",
	Variant = "Secondary",
	WriteAlias = "orch-secondary",
	ReadAlias = "orch-secondary-search",
	DatePattern = "yyyy.MM.dd.HHmmss",
	Configuration = typeof(HashableArticleConfig))]

// ── Semantic search: semantic-articles ──────────────────────────────────
[Index<SemanticArticle>(
	Name = "semantic-articles",
	Configuration = typeof(SemanticArticleConfig))]

// ── AI Enrichment: ai-docs ─────────────────────────────────────────────
[Index<AiDocumentationPage>(
	Name = "ai-docs-primary",
	Variant = "AiPrimary",
	WriteAlias = "ai-docs-primary",
	ReadAlias = "ai-docs-primary-search",
	DatePattern = "yyyy.MM.dd.HHmmss")]
[Index<AiDocumentationPage>(
	Name = "ai-docs-secondary",
	Variant = "AiSecondary",
	WriteAlias = "ai-docs-secondary",
	ReadAlias = "ai-docs-secondary-search",
	DatePattern = "yyyy.MM.dd.HHmmss")]
[AiEnrichment<AiDocumentationPage>(
	Role = "You are a documentation analysis assistant.",
	MatchField = "url")]
public static partial class TestMappingContext;
