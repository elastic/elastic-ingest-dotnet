// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Transport.Products.Elasticsearch;

namespace Elastic.Ingest.Elasticsearch.Serialization;

/// <summary> Represents a bulk response item</summary>
[JsonConverter(typeof(ItemConverter))]
public class BulkResponseItem
{
	/// <summary> The action that was used for the event (create/index) </summary>
	public string Action { get; internal set; } = null!;
	/// <summary> Elasticsearch error if any </summary>
	public ErrorCause? Error { get; internal set; }
	/// <summary> Status code from Elasticsearch writing the event </summary>
	public int Status { get; internal set; }
}

internal class ItemConverter : JsonConverter<BulkResponseItem>
{
	private static readonly BulkResponseItem OkayBulkResponseItem = new BulkResponseItem { Status = 200, Action = "index" };

	public override BulkResponseItem Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		//TODO nasty null return
		if (reader.TokenType != JsonTokenType.StartObject) return null!;

		reader.Read();
		var depth = reader.CurrentDepth;
		var status = 0;
		ErrorCause? error = null;
		var action = reader.GetString()!;
		while (reader.Read() && reader.CurrentDepth >= depth)
		{
			if (reader.TokenType != JsonTokenType.PropertyName) continue;

			var text = reader.GetString();
			switch (text)
			{
				case "status":
					reader.Read();
					status = reader.GetInt32();
					break;
				case "error":
					reader.Read();
					error = JsonSerializer.Deserialize<ErrorCause>(ref reader, options);
					break;
			}
		}
		var r = status == 200
			? OkayBulkResponseItem
			: new BulkResponseItem { Action = action, Status = status, Error = error };

		return r;
	}

	public override void Write(Utf8JsonWriter writer, BulkResponseItem value, JsonSerializerOptions options)
	{
		// ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
		if (value is null)
		{
			writer.WriteNullValue();
			return;
		}

		writer.WriteStartObject();
		writer.WritePropertyName(value.Action);
		writer.WriteStartObject();

		if (value.Error != null)
		{
			writer.WritePropertyName("error");
			JsonSerializer.Serialize(writer, value.Error, options);
		}

		writer.WriteNumber("status", value.Status);
		writer.WriteEndObject();
		writer.WriteEndObject();
	}
}
