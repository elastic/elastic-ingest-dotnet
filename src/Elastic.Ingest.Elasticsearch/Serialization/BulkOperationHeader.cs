// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elastic.Ingest.Elasticsearch.Serialization;

/// <summary> Represents the _bulk operation meta header </summary>
public abstract class BulkOperationHeader
{
	/// <summary> The index or data stream to write to </summary>
	[JsonPropertyName("_index")]
	public string? Index { get; init; }

	/// <summary> The id of the object being written </summary>
	[JsonPropertyName("_id")]
	public string? Id { get; init;  }

	/// <summary> Require <see cref="Index"/> to point to an alias </summary>
	[JsonPropertyName("require_alias")]
	public bool? RequireAlias { get; init; }
}

/// <summary> Represents the _bulk create operation meta header </summary>
[JsonConverter(typeof(BulkOperationHeaderConverter<CreateOperation>))]
public class CreateOperation : BulkOperationHeader
{
	/// <summary>  </summary>
	[JsonPropertyName("dynamic_templates")]
	public Dictionary<string, string>? DynamicTemplates { get; init; }
}

/// <summary> Represents the _bulk index operation meta header </summary>
[JsonConverter(typeof(BulkOperationHeaderConverter<IndexOperation>))]
public class IndexOperation : BulkOperationHeader
{
	/// <summary>  </summary>
	[JsonPropertyName("dynamic_templates")]
	public Dictionary<string, string>? DynamicTemplates { get; init; }
}

/// <summary> Represents the _bulk delete operation meta header </summary>
[JsonConverter(typeof(BulkOperationHeaderConverter<DeleteOperation>))]
public class DeleteOperation : BulkOperationHeader
{
}

/// <summary> Represents the _bulk update operation meta header </summary>
[JsonConverter(typeof(BulkOperationHeaderConverter<UpdateOperation>))]
public class UpdateOperation : BulkOperationHeader
{
}

internal class BulkOperationHeaderConverter<THeader> : JsonConverter<THeader>
	where THeader : BulkOperationHeader
{
	public override THeader Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		throw new NotImplementedException();

	public override void Write(Utf8JsonWriter writer, THeader value, JsonSerializerOptions options)
	{
		var op = value switch
		{
			CreateOperation _ => "create",
			DeleteOperation _ => "delete",
			IndexOperation _ => "index",
			UpdateOperation _ => "update",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
		};
		writer.WriteStartObject();
		writer.WritePropertyName(op);
		writer.WriteStartObject();
		if (!string.IsNullOrWhiteSpace(value.Index))
			writer.WriteString("_index", value.Index);
		if (!string.IsNullOrWhiteSpace(value.Id))
			writer.WriteString("_id", value.Id);
		if (value.RequireAlias == true)
			writer.WriteBoolean("require_alias", true);
		if (value is CreateOperation c)
			WriteDynamicTemplates(writer, options, c.DynamicTemplates);
		if (value is IndexOperation i)
			WriteDynamicTemplates(writer, options, i.DynamicTemplates);

		writer.WriteEndObject();
		writer.WriteEndObject();
	}

	private static void WriteDynamicTemplates(Utf8JsonWriter writer, JsonSerializerOptions options, Dictionary<string, string>? templates)
	{
		if (templates is not { Count: > 0 }) return;

		writer.WritePropertyName("dynamic_templates");
		JsonSerializer.Serialize(writer, templates, options);
	}
}
