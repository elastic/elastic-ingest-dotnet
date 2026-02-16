// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;
using Elastic.Mapping;

namespace Elastic.Ingest.MultiChannel;

/// <summary>
/// A knowledge-base article indexed to both lexical and semantic indices.
/// Demonstrates: [Id], [ContentHash], EntityTarget.Index, IncrementalSyncOrchestrator.
/// </summary>
public class KnowledgeArticle
{
	[Id]
	[Keyword]
	public string Url { get; set; } = string.Empty;

	[Text(Analyzer = "standard")]
	public string Title { get; set; } = string.Empty;

	[Text(Analyzer = "standard")]
	public string Body { get; set; } = string.Empty;

	[ContentHash]
	[Keyword]
	public string Hash { get; set; } = string.Empty;

	[JsonPropertyName("@timestamp")]
	[Timestamp]
	public DateTimeOffset UpdatedAt { get; set; }
}
