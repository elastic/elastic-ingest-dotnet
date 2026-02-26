// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping.Tests;

/// <summary>
/// Verifies CreateContext(...) generation for resolvers using NameTemplate.
/// Covers: placeholder interpolation, env/namespace optional parameter handling,
/// DatePattern propagation, SearchStrategy auto-derivation, and base context immutability.
/// </summary>
public class CreateContextTests
{
	// ── KnowledgeArticle: template with custom + well-known placeholders ──

	[Test]
	public void TemplatedResolverDoesNotExposeContextProperty()
	{
		var resolver = TemplatedMappingContext.KnowledgeArticle;
		var type = resolver.GetType();
		var contextProp = type.GetProperty("Context");
		contextProp.Should().BeNull("templated resolvers expose CreateContext() instead of Context");
	}

	[Test]
	public void CreateContextInterpolatesPlaceholders()
	{
		var ctx = TemplatedMappingContext.KnowledgeArticle.CreateContext("semantic");

		ctx.IndexStrategy!.WriteTarget.Should().NotBeNull();
		ctx.IndexStrategy.WriteTarget.Should().StartWith("docs-semantic-");
	}

	[Test]
	public void CreateContextWithExplicitEnv()
	{
		var ctx = TemplatedMappingContext.KnowledgeArticle.CreateContext("semantic", env: "production");

		ctx.IndexStrategy!.WriteTarget.Should().Be("docs-semantic-production");
	}

	[Test]
	public void CreateContextEnvDefaultsToEnvironmentVariable()
	{
		var expected = ElasticsearchTypeContext.ResolveDefaultNamespace();
		var ctx = TemplatedMappingContext.KnowledgeArticle.CreateContext("lexical");

		ctx.IndexStrategy!.WriteTarget.Should().Be($"docs-lexical-{expected}");
	}

	[Test]
	public void CreateContextCarriesDatePattern()
	{
		var ctx = TemplatedMappingContext.KnowledgeArticle.CreateContext("semantic", env: "staging");

		ctx.IndexStrategy!.DatePattern.Should().Be("yyyy.MM.dd.HHmmss");
	}

	[Test]
	public void CreateContextAutoDerivesSearchPatternWhenDatePatternPresent()
	{
		var ctx = TemplatedMappingContext.KnowledgeArticle.CreateContext("semantic", env: "staging");

		ctx.SearchStrategy!.Pattern.Should().Be("docs-semantic-staging-*",
			"DatePattern present → search pattern = writeTarget + \"-*\"");
	}

	[Test]
	public void CreateContextPreservesEntityTarget()
	{
		var ctx = TemplatedMappingContext.KnowledgeArticle.CreateContext("semantic", env: "prod");

		ctx.EntityTarget.Should().Be(EntityTarget.Index);
	}

	[Test]
	public void CreateContextPreservesMappedType()
	{
		var ctx = TemplatedMappingContext.KnowledgeArticle.CreateContext("semantic", env: "prod");

		ctx.MappedType.Should().Be<KnowledgeArticle>();
	}

	[Test]
	public void CreateContextPreservesHashesAndJson()
	{
		var resolver = TemplatedMappingContext.KnowledgeArticle;
		var ctx = resolver.CreateContext("semantic", env: "prod");

		ctx.Hash.Should().Be(resolver.Hash);
		ctx.SettingsHash.Should().Be(resolver.SettingsHash);
		ctx.MappingsHash.Should().Be(resolver.MappingsHash);
	}

	[Test]
	public void CreateContextPreservesIdAccessor()
	{
		var ctx = TemplatedMappingContext.KnowledgeArticle.CreateContext("semantic", env: "prod");
		var article = new KnowledgeArticle { ArticleId = "abc-123" };

		ctx.GetId.Should().NotBeNull();
		ctx.GetId!(article).Should().Be("abc-123");
	}

	[Test]
	public void CreateContextPreservesTimestampAccessor()
	{
		var ctx = TemplatedMappingContext.KnowledgeArticle.CreateContext("semantic", env: "prod");
		var ts = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
		var article = new KnowledgeArticle { PublishedAt = ts };

		ctx.GetTimestamp.Should().NotBeNull();
		ctx.GetTimestamp!(article).Should().Be(ts);
	}

	[Test]
	public void MultipleCreateContextCallsAreIndependent()
	{
		var ctx1 = TemplatedMappingContext.KnowledgeArticle.CreateContext("semantic", env: "prod");
		var ctx2 = TemplatedMappingContext.KnowledgeArticle.CreateContext("lexical", env: "staging");

		ctx1.IndexStrategy!.WriteTarget.Should().Be("docs-semantic-prod");
		ctx2.IndexStrategy!.WriteTarget.Should().Be("docs-lexical-staging");
	}

	[Test]
	public void ResolveIndexNameAppendsDatedSuffix()
	{
		var ctx = TemplatedMappingContext.KnowledgeArticle.CreateContext("semantic", env: "prod");
		var timestamp = new DateTimeOffset(2025, 6, 15, 14, 30, 45, TimeSpan.Zero);

		var indexName = ctx.ResolveIndexName(timestamp);

		indexName.Should().Be("docs-semantic-prod-2025.06.15.143045");
	}

	// ── KnowledgeArticleMulti: template with only custom placeholders, no DatePattern ──

	[Test]
	public void MultiVariantCreateContextInterpolates()
	{
		var ctx = TemplatedMappingContext.KnowledgeArticleMulti.CreateContext(team: "search", component: "ingest");

		ctx.IndexStrategy!.WriteTarget.Should().Be("articles-search-ingest");
	}

	[Test]
	public void MultiVariantNullDatePatternAndSearchPattern()
	{
		var ctx = TemplatedMappingContext.KnowledgeArticleMulti.CreateContext(team: "search", component: "ingest");

		ctx.IndexStrategy!.DatePattern.Should().BeNull("no DatePattern on this registration");
		ctx.SearchStrategy!.Pattern.Should().BeNull("no DatePattern → no auto-derived search pattern");
	}

	[Test]
	public void MultiVariantIsSeparateResolver()
	{
		var defaultResolver = TemplatedMappingContext.KnowledgeArticle;
		var multiResolver = TemplatedMappingContext.KnowledgeArticleMulti;

		defaultResolver.Should().NotBeSameAs(multiResolver);
		defaultResolver.Hash.Should().Be(multiResolver.Hash, "same type → same mappings hash");
	}

	// ── LocationRecord: template with only a well-known namespace placeholder ──

	[Test]
	public void NamespaceOnlyTemplateDefaultsToEnv()
	{
		var expected = ElasticsearchTypeContext.ResolveDefaultNamespace();
		var ctx = TemplatedMappingContext.LocationRecord.CreateContext();

		ctx.IndexStrategy!.WriteTarget.Should().Be($"geo-{expected}");
	}

	[Test]
	public void NamespaceOnlyTemplateExplicitValue()
	{
		var ctx = TemplatedMappingContext.LocationRecord.CreateContext(@namespace: "us-east-1");

		ctx.IndexStrategy!.WriteTarget.Should().Be("geo-us-east-1");
	}

	[Test]
	public void NamespaceOnlyTemplateNoSearchPattern()
	{
		var ctx = TemplatedMappingContext.LocationRecord.CreateContext(@namespace: "us-east-1");

		ctx.SearchStrategy!.Pattern.Should().BeNull("no DatePattern → no auto-derived search pattern");
	}

	// ── All dictionary for templated context ──

	[Test]
	public void TemplatedContextAllContainsUniqueTypes()
	{
		var all = TemplatedMappingContext.All;

		all.Should().HaveCount(2, "KnowledgeArticle (deduped across variants) + LocationRecord");
		all.Should().ContainKey(typeof(KnowledgeArticle));
		all.Should().ContainKey(typeof(LocationRecord));
	}

	[Test]
	public void TemplatedContextAllMetadataHasValidFieldMappings()
	{
		foreach (var (_, metadata) in TemplatedMappingContext.All)
			metadata.PropertyToField.Should().NotBeEmpty();
	}

	[Test]
	public void TemplatedContextInstanceImplementsMappingContextInterface()
	{
		var instance = TemplatedMappingContext.Instance;

		instance.All.Should().HaveCount(2);
		instance.All.Should().ContainKey(typeof(KnowledgeArticle));
	}
}
