// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping.Tests;

/// <summary>
/// Verifies that all Elasticsearch resource names resolved from
/// <see cref="ElasticsearchTypeContext"/> are lowercased ordinally,
/// as required by Elasticsearch.
/// </summary>
public class LowercaseResourceNameTests
{
	// ── WithIndexName ────────────────────────────────────────────────────

	[Test]
	public void WithIndexName_LowercasesWriteTarget()
	{
		var ctx = TestMappingContext.SimpleDocument.Context.WithIndexName("My-Index-NAME");

		ctx.IndexStrategy!.WriteTarget.Should().Be("my-index-name");
	}

	[Test]
	public void WithIndexName_LowercasesResolvedWriteAlias()
	{
		var ctx = TestMappingContext.SimpleDocument.Context.WithIndexName("My-Index-NAME");

		ctx.ResolveWriteAlias().Should().Be("my-index-name");
	}

	[Test]
	public void WithIndexName_LowercasesResolvedSearchPattern_WhenDatePatternPresent()
	{
		var ctx = ExtendedTestMappingContext.RollingIndex.Context.WithIndexName("Rolling-INDEX");

		ctx.SearchStrategy!.Pattern.Should().Be("rolling-index-*");
	}

	[Test]
	public void WithIndexName_LowercasesResolvedReadTarget()
	{
		var ctx = TestMappingContext.SimpleDocument.Context.WithIndexName("My-Index-NAME");

		ctx.ResolveReadTarget().Should().Be("my-index-name");
	}

	// ── WithNamespace ────────────────────────────────────────────────────

	[Test]
	public void WithNamespace_LowercasesNamespace()
	{
		var ctx = TestMappingContext.NginxAccessLog.Context.WithNamespace("Production");

		ctx.IndexStrategy!.Namespace.Should().Be("production");
	}

	[Test]
	public void WithNamespace_LowercasesDataStreamName()
	{
		var ctx = TestMappingContext.NginxAccessLog.Context.WithNamespace("Production");

		ctx.IndexStrategy!.DataStreamName.Should().Be("logs-nginx.access-production");
	}

	// ── ResolveIndexName ─────────────────────────────────────────────────

	[Test]
	public void ResolveIndexName_LowercasesResult()
	{
		var ctx = ExtendedTestMappingContext.RollingIndex.Context.WithIndexName("ROLLING-INDEX");
		var timestamp = new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero);

		ctx.ResolveIndexName(timestamp).Should().Be("rolling-index-2025.06");
	}

	// ── ResolveIndexFormat ───────────────────────────────────────────────

	[Test]
	public void ResolveIndexFormat_LowercasesWriteTargetInFormatString()
	{
		var ctx = ExtendedTestMappingContext.RollingIndex.Context.WithIndexName("ROLLING-INDEX");

		var format = ctx.ResolveIndexFormat();
		format.Should().StartWith("rolling-index-");
	}

	[Test]
	public void ResolveIndexFormat_PreservesDateFormatSpecifier()
	{
		var ctx = ExtendedTestMappingContext.RollingIndex.Context.WithIndexName("ROLLING-INDEX");

		var format = ctx.ResolveIndexFormat();
		format.Should().Contain("{0:yyyy.MM}");
	}

	[Test]
	public void ResolveIndexFormat_WithBatchTimestamp_LowercasesResult()
	{
		var ctx = ExtendedTestMappingContext.RollingIndex.Context.WithIndexName("ROLLING-INDEX");
		var ts = new DateTimeOffset(2025, 6, 15, 0, 0, 0, TimeSpan.Zero);

		var format = ctx.ResolveIndexFormat(ts);
		format.Should().Be("rolling-index-2025.06");
	}

	[Test]
	public void ResolveIndexFormat_WithoutDatePattern_LowercasesResult()
	{
		var ctx = TestMappingContext.SimpleDocument.Context.WithIndexName("MY-INDEX");

		ctx.ResolveIndexFormat().Should().Be("my-index");
	}

	// ── ResolveWriteAlias ────────────────────────────────────────────────

	[Test]
	public void ResolveWriteAlias_WithDatePattern_LowercasesResult()
	{
		var ctx = ExtendedTestMappingContext.RollingIndex.Context.WithIndexName("ROLLING-INDEX");

		ctx.ResolveWriteAlias().Should().Be("rolling-index-latest");
	}

	[Test]
	public void ResolveWriteAlias_WithoutDatePattern_LowercasesResult()
	{
		var ctx = TestMappingContext.SimpleDocument.Context.WithIndexName("MY-INDEX");

		ctx.ResolveWriteAlias().Should().Be("my-index");
	}

	// ── ResolveReadTarget ────────────────────────────────────────────────

	[Test]
	public void ResolveReadTarget_LowercasesReadAlias()
	{
		var ctx = TestMappingContext.LogEntry.Context;

		ctx.ResolveReadTarget().Should().Be("logs-read");
	}

	// ── ResolveSearchPattern ─────────────────────────────────────────────

	[Test]
	public void ResolveSearchPattern_LowercasesResult()
	{
		var ctx = ExtendedTestMappingContext.RollingIndex.Context.WithIndexName("ROLLING-INDEX");

		ctx.ResolveSearchPattern().Should().Be("rolling-index-*");
	}

	// ── ResolveAliasFormat ───────────────────────────────────────────────

	[Test]
	public void ResolveAliasFormat_WithDatePattern_LowercasesWriteTarget()
	{
		var ctx = ExtendedTestMappingContext.RollingIndex.Context.WithIndexName("ROLLING-INDEX");

		ctx.ResolveAliasFormat().Should().Be("rolling-index-{0}");
	}

	[Test]
	public void ResolveAliasFormat_WithoutDatePattern_LowercasesResult()
	{
		var ctx = TestMappingContext.SimpleDocument.Context.WithIndexName("MY-INDEX");

		ctx.ResolveAliasFormat().Should().Be("my-index");
	}

	// ── ResolveDataStreamName ────────────────────────────────────────────

	[Test]
	public void ResolveDataStreamName_LowercasesResult()
	{
		var ctx = TestMappingContext.NginxAccessLog.Context.WithNamespace("PRODUCTION");

		ctx.ResolveDataStreamName().Should().Be("logs-nginx.access-production");
	}

	// ── ResolveTemplateName ──────────────────────────────────────────────

	[Test]
	public void ResolveTemplateName_LowercasesResult()
	{
		var ctx = TestMappingContext.SimpleDocument.Context.WithIndexName("MY-INDEX");

		ctx.ResolveTemplateName().Should().Be("my-index-template");
	}

	[Test]
	public void ResolveTemplateName_DataStream_LowercasesResult()
	{
		var ctx = TestMappingContext.NginxAccessLog.Context;

		ctx.ResolveTemplateName().Should().Be("logs-nginx.access");
	}

	// ── CreateContext (generated) ────────────────────────────────────────

	[Test]
	public void CreateContext_LowercasesInterpolatedName()
	{
		var ctx = TemplatedMappingContext.KnowledgeArticle.CreateContext("SEMANTIC", env: "PRODUCTION");

		ctx.IndexStrategy!.WriteTarget.Should().Be("docs-semantic-production");
	}

	[Test]
	public void CreateContext_LowercasesSearchPattern()
	{
		var ctx = TemplatedMappingContext.KnowledgeArticle.CreateContext("SEMANTIC", env: "PRODUCTION");

		ctx.SearchStrategy!.Pattern.Should().Be("docs-semantic-production-*");
	}

	[Test]
	public void CreateContext_ResolveIndexName_LowercasesResult()
	{
		var ctx = TemplatedMappingContext.KnowledgeArticle.CreateContext("SEMANTIC", env: "PRODUCTION");
		var ts = new DateTimeOffset(2025, 6, 15, 14, 30, 45, TimeSpan.Zero);

		ctx.ResolveIndexName(ts).Should().Be("docs-semantic-production-2025.06.15.143045");
	}
}
