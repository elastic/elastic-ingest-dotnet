// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

/// <summary>
/// Central mapping context for all integration test scenarios.
/// Each entity gets a unique prefix to isolate Elasticsearch resources per test group.
/// <para>
/// Access type contexts via generated properties:
/// <list type="bullet">
///   <item><c>TestMappingContext.ServerMetricsEvent.Context</c> — data stream</item>
///   <item><c>TestMappingContext.ProductCatalog.Context</c> — plain index</item>
///   <item><c>TestMappingContext.ProductCatalogCatalog.Context</c> — aliased index with hash reuse</item>
///   <item><c>TestMappingContext.HashableArticle.Context</c> — catalog index with hash upserts</item>
///   <item><c>TestMappingContext.SemanticArticle.Context</c> — semantic index with inference</item>
/// </list>
/// </para>
/// </summary>
[ElasticsearchMappingContext]
[Entity<ServerMetricsEvent>(
	Target = EntityTarget.DataStream,
	Type = "logs",
	Dataset = "srvmetrics",
	Namespace = "default")]
[Entity<ProductCatalog>(Target = EntityTarget.Index, Name = "idx-products", RefreshInterval = "1s")]
[Entity<ProductCatalog>(
	Target = EntityTarget.Index,
	Name = "cat-products",
	Variant = "Catalog",
	WriteAlias = "cat-products",
	ReadAlias = "cat-products-search",
	SearchPattern = "cat-products-*",
	DatePattern = "yyyy.MM.dd.HHmmss")]
[Entity<HashableArticle>(Target = EntityTarget.Index, Name = "hashable-articles")]
[Entity<SemanticArticle>(Target = EntityTarget.Index, Name = "semantic-articles")]
public static partial class TestMappingContext;
