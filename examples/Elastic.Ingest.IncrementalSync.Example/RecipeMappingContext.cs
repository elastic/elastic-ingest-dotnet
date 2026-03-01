// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping;
using Elastic.Mapping.Mappings;

namespace Elastic.Ingest.IncrementalSync.Example;

[ElasticsearchMappingContext]
[Index<RecipeDocument>(
	NameTemplate = "recipes-lexical-{env}",
	DatePattern = "yyyy.MM.dd.HHmmss"
)]
[Index<RecipeDocument>(
	NameTemplate = "recipes-semantic-{env}",
	Variant = "Semantic",
	DatePattern = "yyyy.MM.dd.HHmmss"
)]
[AiEnrichment<RecipeDocument>(
	Role = "You are a culinary assistant that analyzes recipe content.",
	MatchField = "slug"
)]
public static partial class RecipeMappingContext
{
	public static MappingsBuilder<RecipeDocument> ConfigureRecipeDocumentSemanticMappings(
		MappingsBuilder<RecipeDocument> mappings) =>
		mappings
			.Description(f => f.MultiField("semantic", mf => mf.SemanticText().InferenceId(".elser-2-elastic")));
}
