// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using FluentAssertions;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests.Strategies;

/*
 * Tests: ElasticsearchTypeContext resolve methods
 *
 * No Elasticsearch cluster required — pure unit tests.
 *
 *   ResolveWriteAlias()
 *   ├── No DatePattern  → WriteTarget       ("idx-products")
 *   └── Has DatePattern → WriteTarget-latest ("cat-products-latest")
 *
 *   ResolveReadTarget()
 *   ├── Has ReadAlias  → ReadAlias          ("cat-products-search")
 *   └── No ReadAlias   → falls back to ResolveWriteAlias()
 */
public class TypeContextResolverTests
{
	[Test]
	public void WriteAliasForFixedIndexIsWriteTarget()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		var alias = ctx.ResolveWriteAlias();
		alias.Should().Be("idx-products",
			"no DatePattern → WriteTarget used directly");
	}

	[Test]
	public void WriteAliasForDateRollingIndexAppendsLatest()
	{
		var ctx = TestMappingContext.ProductCatalogCatalog.Context;
		var alias = ctx.ResolveWriteAlias();
		alias.Should().Be("cat-products-latest",
			"DatePattern set → WriteTarget + '-latest'");
	}

	[Test]
	public void ReadTargetFallsBackToWriteAlias()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		var target = ctx.ResolveReadTarget();
		target.Should().Be("idx-products",
			"no ReadAlias → falls back to write alias");
	}

	[Test]
	public void ReadTargetUsesReadAliasWhenAvailable()
	{
		var ctx = TestMappingContext.ProductCatalogCatalog.Context;
		var target = ctx.ResolveReadTarget();
		target.Should().Be("cat-products-search",
			"Catalog variant has ReadAlias = 'cat-products-search'");
	}

	[Test]
	public void WriteAliasForHashableArticleIsWriteTarget()
	{
		var ctx = TestMappingContext.HashableArticle.Context;
		var alias = ctx.ResolveWriteAlias();
		alias.Should().Be("hashable-articles");
	}

	[Test]
	public void ResolveIndexNameWithDatePattern()
	{
		var ctx = TestMappingContext.ProductCatalogCatalog.Context;
		var ts = new DateTimeOffset(2026, 2, 24, 14, 30, 55, TimeSpan.Zero);
		var name = ctx.ResolveIndexName(ts);
		name.Should().Be("cat-products-2026.02.24.143055");
	}

	[Test]
	public void ResolveSearchPatternForDataStream()
	{
		var ctx = TestMappingContext.ServerMetricsEvent.Context;
		var pattern = ctx.ResolveSearchPattern();
		pattern.Should().Be("logs-srvmetrics-*");
	}

	[Test]
	public void ResolveSearchPatternForRollingIndex()
	{
		var ctx = TestMappingContext.ProductCatalogCatalog.Context;
		var pattern = ctx.ResolveSearchPattern();
		pattern.Should().Be("cat-products-*");
	}

	[Test]
	public void ResolveSearchPatternForFixedIndex()
	{
		var ctx = TestMappingContext.ProductCatalog.Context;
		var pattern = ctx.ResolveSearchPattern();
		pattern.Should().Be("idx-products*");
	}
}
