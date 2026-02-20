// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

/// <summary>
/// Central mapping context for all integration test scenarios.
/// Each entity gets a unique prefix to isolate Elasticsearch resources per test group.
/// Analysis and mapping customization is externalized via <c>Configuration = typeof(...)</c>.
/// </summary>
[ElasticsearchMappingContext]
[Entity<ServerMetricsEvent>(
	Target = EntityTarget.DataStream,
	Type = "logs",
	Dataset = "srvmetrics",
	Namespace = "default",
	Configuration = typeof(ServerMetricsEventConfig))]
[Entity<ProductCatalog>(
	Target = EntityTarget.Index,
	Name = "idx-products",
	RefreshInterval = "1s",
	Configuration = typeof(ProductCatalogConfig))]
[Entity<ProductCatalog>(
	Target = EntityTarget.Index,
	Name = "cat-products",
	Variant = "Catalog",
	WriteAlias = "cat-products",
	ReadAlias = "cat-products-search",
	SearchPattern = "cat-products-*",
	DatePattern = "yyyy.MM.dd.HHmmss",
	Configuration = typeof(ProductCatalogConfig))]
[Entity<HashableArticle>(
	Target = EntityTarget.Index,
	Name = "hashable-articles",
	Configuration = typeof(HashableArticleConfig))]
[Entity<SemanticArticle>(
	Target = EntityTarget.Index,
	Name = "semantic-articles",
	Configuration = typeof(SemanticArticleConfig))]
public static partial class TestMappingContext;
