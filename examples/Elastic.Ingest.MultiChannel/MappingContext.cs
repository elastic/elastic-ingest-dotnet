// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping;

namespace Elastic.Ingest.MultiChannel;

[ElasticsearchMappingContext]
[Entity<KnowledgeArticle>(
	Target = EntityTarget.Index,
	Name = "knowledge-lexical",
	WriteAlias = "knowledge-lexical",
	ReadAlias = "knowledge-lexical-search",
	SearchPattern = "knowledge-lexical-*",
	DatePattern = "yyyy.MM.dd.HHmmss"
)]
[Entity<KnowledgeArticle>(
	Target = EntityTarget.Index,
	Name = "knowledge-semantic",
	Variant = "Semantic",
	WriteAlias = "knowledge-semantic",
	ReadAlias = "knowledge-semantic-search",
	SearchPattern = "knowledge-semantic-*",
	DatePattern = "yyyy.MM.dd.HHmmss"
)]
public static partial class ExampleMappingContext;
