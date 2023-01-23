// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Text.Json.Serialization;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

public class TimeSeriesDocument
{
	[JsonPropertyName("@timestamp")]
	public DateTimeOffset Timestamp { get; set; }

	[JsonPropertyName("message")]
	public string Message { get; set; }
}

public class CatalogDocument
{
	[JsonPropertyName("id")]
	public string Id { get; set; }

	[JsonPropertyName("title")]
	public string Title { get; set; }

	[JsonPropertyName("created")]
	public DateTimeOffset Created { get; set; }
}
