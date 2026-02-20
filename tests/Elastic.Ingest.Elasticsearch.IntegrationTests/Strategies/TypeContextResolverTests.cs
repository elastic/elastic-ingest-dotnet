// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using FluentAssertions;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Strategies;

/*
 * Tests: TypeContextResolver — static utility that resolves index names
 *        from ElasticsearchTypeContext metadata
 *
 * Source: src/Elastic.Ingest.Elasticsearch/TypeContextResolver.cs
 *
 * No Elasticsearch cluster required — pure unit tests.
 *
 *   ResolveWriteAlias(ctx)
 *   ├── No DatePattern  → WriteTarget       ("idx-products")
 *   └── Has DatePattern → WriteTarget-latest ("cat-products-latest")
 *
 *   ResolveReadTarget(ctx)
 *   ├── Has ReadAlias  → ReadAlias          ("cat-products-search")
 *   └── No ReadAlias   → falls back to ResolveWriteAlias()
 */
public class TypeContextResolverTests
{
	[Test]
	public void WriteAliasForFixedIndexIsWriteTarget()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		var alias = TypeContextResolver.ResolveWriteAlias(ctx);
		alias.Should().Be("idx-products",
			"no DatePattern → WriteTarget used directly");
	}

	[Test]
	public void WriteAliasForDateRollingIndexAppendsLatest()
	{
		var ctx = TestMappingContext.ProductCatalogCatalog.Context;
		var alias = TypeContextResolver.ResolveWriteAlias(ctx);
		alias.Should().Be("cat-products-latest",
			"DatePattern set → WriteTarget + '-latest'");
	}

	[Test]
	public void ReadTargetFallsBackToWriteAlias()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		var target = TypeContextResolver.ResolveReadTarget(ctx);
		target.Should().Be("idx-products",
			"no ReadAlias → falls back to write alias");
	}

	[Test]
	public void ReadTargetUsesReadAliasWhenAvailable()
	{
		var ctx = TestMappingContext.ProductCatalogCatalog.Context;
		var target = TypeContextResolver.ResolveReadTarget(ctx);
		target.Should().Be("cat-products-search",
			"Catalog variant has ReadAlias = 'cat-products-search'");
	}

	[Test]
	public void WriteAliasForHashableArticleIsWriteTarget()
	{
		var ctx = TestMappingContext.HashableArticle.Context;
		var alias = TypeContextResolver.ResolveWriteAlias(ctx);
		alias.Should().Be("hashable-articles");
	}

	[Test]
	public void ReadTargetForDataStreamThrows()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var act = () => TypeContextResolver.ResolveWriteAlias(ctx);
		act.Should().Throw<InvalidOperationException>(
			"DataStream entity has no IndexStrategy.WriteTarget");
	}
}
