// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Transport.Products.Elasticsearch;

namespace Elastic.Ingest.Elasticsearch.Serialization;

/// <summary>Represents the _bulk response from Elasticsearch</summary>
public class BulkResponse : ElasticsearchResponse
{
	/// <summary>
	/// Individual bulk response items information
	/// </summary>
	[JsonPropertyName("items")]
	[JsonConverter(typeof(ResponseItemsConverter))]
	public IReadOnlyCollection<BulkResponseItem> Items { get; set; } = null!;

	/// <summary> Overall bulk error from Elasticsearch if any</summary>
	[JsonPropertyName("error")]
	public ErrorCause? Error { get; set; }

	/// <summary>
	/// Tries and get the error from Elasticsearch as string
	/// </summary>
	/// <returns>True if Elasticsearch contained an overall bulk error</returns>
	public bool TryGetServerErrorReason(out string? reason)
	{
		reason = Error?.Reason;
		return !string.IsNullOrWhiteSpace(reason);
	}
}

internal class ResponseItemsConverter : JsonConverter<IReadOnlyCollection<BulkResponseItem>>
{
	public static readonly IReadOnlyCollection<BulkResponseItem> EmptyBulkItems =
		new ReadOnlyCollection<BulkResponseItem>(new List<BulkResponseItem>());

	public override IReadOnlyCollection<BulkResponseItem> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.StartArray) return EmptyBulkItems;

		var list = new List<BulkResponseItem>();
		var depth = reader.CurrentDepth;
		while (reader.Read() && reader.CurrentDepth > depth)
		{
			var item = JsonSerializer.Deserialize<BulkResponseItem>(ref reader, IngestSerializationContext.Default.BulkResponseItem);
			if (item != null)
				list.Add(item);
		}
		return new ReadOnlyCollection<BulkResponseItem>(list);
	}

	public override void Write(Utf8JsonWriter writer, IReadOnlyCollection<BulkResponseItem> value, JsonSerializerOptions options) =>
		throw new NotImplementedException();
}
