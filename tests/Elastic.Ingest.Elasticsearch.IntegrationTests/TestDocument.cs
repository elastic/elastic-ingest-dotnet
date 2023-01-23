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
