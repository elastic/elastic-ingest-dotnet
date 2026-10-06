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

	/// <summary>
	/// The <c>_id</c> of the document, which is the only way to learn an id Elasticsearch generated.
	/// <c>null</c> when the response did not include it: it is only requested when needed,
	/// see <see cref="Bulk.BulkItemIdentity"/>, and it is not known for requests that failed as a whole.
	/// </summary>
	public string? Id { get; internal set; }

	/// <summary>
	/// The concrete <c>_index</c> that received the document, for example the backing index behind an alias or data stream.
	/// <c>null</c> when the response did not include it, see <see cref="Id"/>.
	/// </summary>
	public string? Index { get; internal set; }
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
		string? lastIndex = null;
		return ReadItem(ref reader, ref lastIndex)!;
	}

	/// <summary>
	/// Reads one <c>{"index":{...}}</c> item and leaves the reader on its closing brace.
	/// <paramref name="lastIndex"/> lets a caller reading many items reuse the index string: items of one request mostly share it.
	/// </summary>
	internal static BulkResponseItem? ReadItem(ref Utf8JsonReader reader, ref string? lastIndex)
	{
		//TODO nasty null return
		if (reader.TokenType != JsonTokenType.StartObject) return null;

		reader.Read();
		var depth = reader.CurrentDepth;
		var status = 0;
		ErrorCause? error = null;
		string? id = null, index = null;
		var action = ReadActionName(ref reader);
		while (reader.Read() && reader.CurrentDepth >= depth)
		{
			// Values we do not use, or that have an unexpected type, are stepped over whole below, so only property names
			// of the action object itself are ever visited and a nested object such as _shards can never be mistaken for it.
			if (reader.TokenType != JsonTokenType.PropertyName) continue;

			if (reader.ValueTextEquals("status"u8))
			{
				reader.Read();
				status = reader.GetInt32();
			}
			else if (reader.ValueTextEquals("error"u8))
			{
				reader.Read();
				error = JsonSerializer.Deserialize<ErrorCause>(ref reader, ElasticsearchTransportSerializerContext.Default.ErrorCause);
			}
			else if (reader.ValueTextEquals("_id"u8))
			{
				reader.Read();
				if (reader.TokenType == JsonTokenType.String) id = reader.GetString();
				else reader.Skip();
			}
			else if (reader.ValueTextEquals("_index"u8))
			{
				reader.Read();
				if (reader.TokenType == JsonTokenType.String)
				{
					if (lastIndex is not null && reader.ValueTextEquals(lastIndex)) index = lastIndex;
					else index = lastIndex = reader.GetString();
				}
				else reader.Skip();
			}
			else
			{
				// not interesting: step over the value, including nested objects and arrays
				reader.Read();
				reader.Skip();
			}
		}

		// Items without per item state (no error, no id, no index) are shared instead of allocated.
		if (error is null && id is null && index is null && GetSuccessItem(action, status) is { } shared)
			return shared;

		return new BulkResponseItem { Action = action, Status = status, Error = error, Id = id, Index = index };
	}

	private static string ReadActionName(ref Utf8JsonReader reader)
	{
		if (reader.ValueTextEquals("index"u8)) return ActionNames[0];
		if (reader.ValueTextEquals("create"u8)) return ActionNames[1];
		if (reader.ValueTextEquals("update"u8)) return ActionNames[2];
		if (reader.ValueTextEquals("delete"u8)) return ActionNames[3];
		return reader.GetString()!;
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

		if (value.Index != null) writer.WriteString("_index", value.Index);
		if (value.Id != null) writer.WriteString("_id", value.Id);
		writer.WriteNumber("status", value.Status);
		writer.WriteEndObject();
		writer.WriteEndObject();
	}
}
