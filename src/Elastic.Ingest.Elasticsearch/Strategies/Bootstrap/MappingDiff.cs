// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// The additive part of a mapping that an existing index is missing.
/// </summary>
/// <param name="Json">
/// A body suitable for <c>PUT {index}/_mapping</c> (<c>{"properties":{…},"runtime":{…}}</c>),
/// or <see langword="null"/> when the index already has everything.
/// </param>
/// <param name="Conflicts">
/// Fields that exist in the index with a different type than the desired mapping. A <c>PUT _mapping</c>
/// cannot change these, so they are reported and left out of <paramref name="Json"/>.
/// </param>
internal sealed record MappingDelta(string? Json, IReadOnlyList<string> Conflicts)
{
	public static readonly MappingDelta Empty = new(null, Array.Empty<string>());

	public bool IsEmpty => Json is null;
}

/// <summary>
/// Computes which fields of a desired mapping an existing index does not have yet.
/// <para>
/// Works on the JSON text only (<see cref="JsonDocument"/> / <see cref="JsonElement"/>), so it needs no
/// reflection and is trim and AOT safe. Field names are already resolved when the desired mapping is
/// generated (<c>[JsonPropertyName]</c>, naming policy, <c>[JsonIgnore]</c>), so this compares JSON names to JSON names.
/// </para>
/// </summary>
internal static class MappingDiff
{
	/// <param name="desiredMappingsJson">The mappings object, as returned by <c>ElasticsearchTypeContext.GetMappingsJson</c>.</param>
	/// <param name="liveMappingResponseJson">The response of <c>GET {index}/_mapping</c>.</param>
	public static MappingDelta ComputeAdditions(string desiredMappingsJson, string liveMappingResponseJson)
	{
		using var desiredDoc = JsonDocument.Parse(desiredMappingsJson);
		using var liveDoc = JsonDocument.Parse(liveMappingResponseJson);

		// {"<index>": {"mappings": {...}}}. An alias can resolve to several indices; a field only counts as
		// present when every one of them has it, so that a PUT to the alias fills the gaps in all of them.
		var lives = new List<JsonElement>();
		if (liveDoc.RootElement.ValueKind == JsonValueKind.Object)
		{
			foreach (var index in liveDoc.RootElement.EnumerateObject())
			{
				if (index.Value.ValueKind == JsonValueKind.Object
					&& index.Value.TryGetProperty("mappings", out var mappings)
					&& mappings.ValueKind == JsonValueKind.Object)
					lives.Add(mappings);
			}
		}
		if (lives.Count == 0 || desiredDoc.RootElement.ValueKind != JsonValueKind.Object)
			return MappingDelta.Empty;

		var desired = desiredDoc.RootElement;
		var conflicts = new List<string>();
		var parts = new List<string>();

		var properties = DiffMap(Child(desired, "properties"), lives.Select(l => Child(l, "properties")).ToList(), string.Empty, conflicts);
		if (properties is not null)
			parts.Add($"\"properties\":{{{properties}}}");

		var runtime = DiffRuntime(Child(desired, "runtime"), lives.Select(l => Child(l, "runtime")).ToList());
		if (runtime is not null)
			parts.Add($"\"runtime\":{{{runtime}}}");

		return parts.Count == 0 && conflicts.Count == 0
			? MappingDelta.Empty
			: new MappingDelta(parts.Count == 0 ? null : $"{{{string.Join(",", parts)}}}", conflicts);
	}

	/// <summary>
	/// Diffs a <c>properties</c> (or multi-field <c>fields</c>) map. Returns the members to add as JSON object
	/// content without the surrounding braces, or <see langword="null"/> when nothing is missing.
	/// </summary>
	private static string? DiffMap(JsonElement desired, List<JsonElement> lives, string path, List<string> conflicts)
	{
		if (desired.ValueKind != JsonValueKind.Object)
			return null;

		var additions = new List<string>();
		foreach (var field in desired.EnumerateObject())
		{
			var fieldPath = path.Length == 0 ? field.Name : $"{path}.{field.Name}";
			var name = JsonEncodedText.Encode(field.Name).ToString();
			var desiredDef = field.Value;

			var liveDefs = new List<JsonElement>(lives.Count);
			var missing = false;
			foreach (var live in lives)
			{
				if (live.ValueKind == JsonValueKind.Object && live.TryGetProperty(field.Name, out var liveDef))
					liveDefs.Add(liveDef);
				else
					missing = true;
			}

			if (missing)
			{
				additions.Add($"\"{name}\":{desiredDef.GetRawText()}");
				continue;
			}

			var desiredType = TypeOf(desiredDef);
			var typeConflict = liveDefs.Select(TypeOf)
				.FirstOrDefault(t => t is not null && desiredType is not null && !string.Equals(t, desiredType, StringComparison.Ordinal));
			if (typeConflict is not null)
			{
				conflicts.Add($"{fieldPath}: index has `{typeConflict}`, mapping wants `{desiredType}`");
				continue;
			}

			// Present on every live index with the same type: look for missing children and multi-fields.
			var children = DiffMap(Child(desiredDef, "properties"), liveDefs.Select(d => Child(d, "properties")).ToList(), fieldPath, conflicts);
			var subFields = DiffMap(Child(desiredDef, "fields"), liveDefs.Select(d => Child(d, "fields")).ToList(), fieldPath, conflicts);
			if (children is null && subFields is null)
				continue;

			// ES needs the type on a field that is updated, except for plain objects (which have none in the mapping).
			var members = new List<string>();
			if (desiredType is not null && desiredType != "object")
				members.Add($"\"type\":\"{JsonEncodedText.Encode(desiredType)}\"");
			if (children is not null)
				members.Add($"\"properties\":{{{children}}}");
			if (subFields is not null)
				members.Add($"\"fields\":{{{subFields}}}");
			additions.Add($"\"{name}\":{{{string.Join(",", members)}}}");
		}

		return additions.Count == 0 ? null : string.Join(",", additions);
	}

	/// <summary> Runtime fields: only the ones the index does not have by name. Existing ones are left alone. </summary>
	private static string? DiffRuntime(JsonElement desired, List<JsonElement> lives)
	{
		if (desired.ValueKind != JsonValueKind.Object)
			return null;

		var additions = new StringBuilder();
		foreach (var field in desired.EnumerateObject())
		{
			if (lives.All(l => l.ValueKind == JsonValueKind.Object && l.TryGetProperty(field.Name, out _)))
				continue;
			if (additions.Length > 0)
				additions.Append(',');
			additions.Append('"').Append(JsonEncodedText.Encode(field.Name)).Append("\":").Append(field.Value.GetRawText());
		}
		return additions.Length == 0 ? null : additions.ToString();
	}

	/// <summary>
	/// The type of a field definition. Elasticsearch leaves <c>type</c> out for <c>object</c> fields that have
	/// <c>properties</c>, so those are reported as <c>object</c>.
	/// </summary>
	private static string? TypeOf(JsonElement definition)
	{
		if (definition.ValueKind != JsonValueKind.Object)
			return null;
		if (definition.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
			return type.GetString();
		return definition.TryGetProperty("properties", out _) ? "object" : null;
	}

	private static JsonElement Child(JsonElement parent, string name) =>
		parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var child) ? child : default;
}
