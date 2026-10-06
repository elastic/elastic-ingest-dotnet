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

internal sealed class ItemConverter : JsonConverter<BulkResponseItem>
{
	private static readonly string[] ActionNames = ["index", "create", "update", "delete"];

	// Successful items carry no per item state, so they are shared instead of allocated per response item.
	private static readonly BulkResponseItem[] SuccessItems = CreateSuccessItems();

	private static BulkResponseItem[] CreateSuccessItems()
	{
		var items = new BulkResponseItem[4 * 2];
		for (var a = 0; a < 4; a++)
		{
			items[a * 2] = new BulkResponseItem { Status = 200, Action = ActionNames[a] };
			items[a * 2 + 1] = new BulkResponseItem { Status = 201, Action = ActionNames[a] };
		}
		return items;
	}

	private static BulkResponseItem? GetSuccessItem(string action, int status)
	{
		if (status != 200 && status != 201) return null;
		var a = action switch { "index" => 0, "create" => 1, "update" => 2, "delete" => 3, _ => -1 };
		return a < 0 ? null : SuccessItems[a * 2 + (status == 200 ? 0 : 1)];
	}

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
					error = JsonSerializer.Deserialize<ErrorCause>(ref reader, ElasticsearchTransportSerializerContext.Default.ErrorCause);
					break;
			}
		}
		var r = (error is null ? GetSuccessItem(action, status) : null)
			?? new BulkResponseItem { Action = action, Status = status, Error = error };

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
			JsonSerializer.Serialize(writer, value.Error, ElasticsearchTransportSerializerContext.Default.ErrorCause);
		}

		writer.WriteNumber("status", value.Status);
		writer.WriteEndObject();
		writer.WriteEndObject();
	}
}
