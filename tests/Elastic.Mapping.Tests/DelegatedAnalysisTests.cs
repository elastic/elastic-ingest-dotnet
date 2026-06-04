// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Mappings;

namespace Elastic.Mapping.Tests;

/// <summary>
/// Tests for the delegation-following analysis parser and base-type-anchored accessor emission.
/// Verifies that analysis component names defined in a separate shared factory class are
/// discovered transitively via ConfigureAnalysis delegation and exposed as source-generated
/// keys reachable from generic code constrained on the base document type.
/// </summary>
public class DelegatedAnalysisTests
{
	// ──────────────────────────────────────────────────────────────────────────────
	// Static accessor: SearchBaseDocumentAnalysis.{Kind}.Name
	// ──────────────────────────────────────────────────────────────────────────────

	[Test]
	public void StaticAccessor_Normalizer_KeywordNormalizer()
	{
		// const-resolved: SearchAnalysisFactory.KeywordNormalizerName = "keyword_normalizer"
		SearchBaseDocumentAnalysis.Normalizers.KeywordNormalizer
			.Should().Be("keyword_normalizer");
	}

	[Test]
	public void StaticAccessor_Analyzers_StartsWithAnalyzer()
	{
		SearchBaseDocumentAnalysis.Analyzers.StartsWithAnalyzer
			.Should().Be("starts_with_analyzer");
	}

	[Test]
	public void StaticAccessor_Analyzers_StartsWithAnalyzerSearch()
	{
		SearchBaseDocumentAnalysis.Analyzers.StartsWithAnalyzerSearch
			.Should().Be("starts_with_analyzer_search");
	}

	[Test]
	public void StaticAccessor_Analyzers_SynonymsAnalyzer_TransitiveDelegation()
	{
		// "synonyms_analyzer" is defined in BuildExtendedAnalysis, which is reached
		// transitively from the SearchProductConfig.ConfigureAnalysis delegation.
		// Both docs share the same anchor so the accessor covers the union of all names.
		SearchBaseDocumentAnalysis.Analyzers.SynonymsAnalyzer
			.Should().Be("synonyms_analyzer");
	}

	[Test]
	public void StaticAccessor_TokenFilters_EnglishStop()
	{
		SearchBaseDocumentAnalysis.TokenFilters.EnglishStop
			.Should().Be("english_stop");
	}

	[Test]
	public void StaticAccessor_TokenFilters_SynonymsFilter()
	{
		// Defined in BuildExtendedAnalysis — transitive delegation.
		SearchBaseDocumentAnalysis.TokenFilters.SynonymsFilter
			.Should().Be("synonyms_filter");
	}

	[Test]
	public void StaticAccessor_CharFilters_StripNonWord()
	{
		SearchBaseDocumentAnalysis.CharFilters.StripNonWord
			.Should().Be("strip_non_word");
	}

	[Test]
	public void StaticAccessor_Tokenizers_StartsWithTokenizer()
	{
		SearchBaseDocumentAnalysis.Tokenizers.StartsWithTokenizer
			.Should().Be("starts_with_tokenizer");
	}

	// ──────────────────────────────────────────────────────────────────────────────
	// Surface parity: both surfaces return the same string value
	// ──────────────────────────────────────────────────────────────────────────────

	[Test]
	public void GenericExtension_Normalizers_MatchesStaticAccessor()
	{
		// Surface (2) == surface (1) — both return the raw ES name.
		var viaExtension = UseAnalysisKeys_Normalizer();
		viaExtension.Should().Be(SearchBaseDocumentAnalysis.Normalizers.KeywordNormalizer);
	}

	[Test]
	public void GenericExtension_Analyzers_MatchesStaticAccessor()
	{
		var viaExtension = UseAnalysisKeys_Analyzer();
		viaExtension.Should().Be(SearchBaseDocumentAnalysis.Analyzers.StartsWithAnalyzer);
	}

	// Helpers that exercise the generic-constrained extension path at the type level.
	// These helpers only compile if the generator emitted the `where TDoc : SearchBaseDocument` extensions.
	private static string UseAnalysisKeys_Normalizer<T>(MappingsBuilder<T> m) where T : SearchBaseDocument
		=> m.Normalizers().KeywordNormalizer;

	private static string UseAnalysisKeys_Normalizer()
	{
		var config = new SearchArticleConfig();
		return config.ConfigureMappings(new MappingsBuilder<SearchArticle>()).Normalizers().KeywordNormalizer;
	}

	private static string UseAnalysisKeys_Analyzer()
	{
		var config = new SearchArticleConfig();
		return config.ConfigureMappings(new MappingsBuilder<SearchArticle>()).Analyzers().StartsWithAnalyzer;
	}

	// ──────────────────────────────────────────────────────────────────────────────
	// Built-in pass-through: base accessor still exposes built-in names
	// ──────────────────────────────────────────────────────────────────────────────

	[Test]
	public void BuiltIn_Analyzers_Standard_StillAccessible()
	{
		SearchBaseDocumentAnalysis.Analyzers.Standard
			.Should().Be("standard");
	}

	[Test]
	public void BuiltIn_Tokenizers_Whitespace_StillAccessible()
	{
		SearchBaseDocumentAnalysis.Tokenizers.Whitespace
			.Should().Be("whitespace");
	}

	// ──────────────────────────────────────────────────────────────────────────────
	// Dedup: two different registered types share one anchored accessor (no CS0101)
	// This is a compile-time check — if the generator emitted the accessor twice,
	// the build above would have failed with CS0101 / CS0436.
	// ──────────────────────────────────────────────────────────────────────────────

	[Test]
	public void Dedup_BothConfigsShareSameAccessor()
	{
		// Both SearchArticle and SearchProduct delegate analysis — anchored to SearchBaseDocument.
		// The accessor class is emitted once and is the same static class.
		var fromArticleCtx = SearchBaseDocumentAnalysis.Normalizers.KeywordNormalizer;
		var fromProductCtx = SearchBaseDocumentAnalysis.Normalizers.KeywordNormalizer;
		fromArticleCtx.Should().Be(fromProductCtx);
	}
}
