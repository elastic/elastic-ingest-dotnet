// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping;

namespace Elastic.Ingest.MultiChannel;

[ElasticsearchMappingContext]
[Index<KnowledgeArticle>(
	Name = "knowledge-lexical",
	WriteAlias = "knowledge-lexical",
	ReadAlias = "knowledge-lexical-search",
	DatePattern = "yyyy.MM.dd.HHmmss"
)]
[Index<KnowledgeArticle>(
	Name = "knowledge-semantic",
	Variant = "Semantic",
	WriteAlias = "knowledge-semantic",
	ReadAlias = "knowledge-semantic-search",
	DatePattern = "yyyy.MM.dd.HHmmss"
)]
public static partial class ExampleMappingContext;
