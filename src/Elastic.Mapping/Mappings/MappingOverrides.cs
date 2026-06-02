// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Nodes;
using Elastic.Mapping.Mappings.Definitions;

namespace Elastic.Mapping.Mappings;

/// <summary>
/// Immutable mapping overrides configuration.
/// Contains field overrides, runtime fields, and dynamic templates.
/// </summary>
public sealed class MappingOverrides
{
	/// <summary>The configured field overrides by field path.</summary>
	public IReadOnlyDictionary<string, IFieldDefinition> Fields { get; }

	/// <summary>The configured runtime fields.</summary>
	public IReadOnlyDictionary<string, RuntimeFieldDefinition> RuntimeFields { get; }

	/// <summary>The configured dynamic templates.</summary>
	public IReadOnlyList<DynamicTemplateDefinition> DynamicTemplates { get; }

	/// <summary>The container intent per field path (internal; not part of the public contract).</summary>
	internal IReadOnlyDictionary<string, FieldContainer> FieldContainers { get; }

	internal MappingOverrides(
		IReadOnlyDictionary<string, IFieldDefinition> fields,
		IReadOnlyDictionary<string, RuntimeFieldDefinition> runtimeFields,
		IReadOnlyList<DynamicTemplateDefinition> dynamicTemplates,
		IReadOnlyDictionary<string, FieldContainer>? fieldContainers = null)
	{
		Fields = fields;
		RuntimeFields = runtimeFields;
		DynamicTemplates = dynamicTemplates;
		FieldContainers = fieldContainers ?? new Dictionary<string, FieldContainer>();
	}

	/// <summary>Returns true if any mapping overrides are configured.</summary>
	public bool HasConfiguration =>
		Fields.Count > 0 ||
		RuntimeFields.Count > 0 ||
		DynamicTemplates.Count > 0;

	/// <summary>
	/// Merges these mapping overrides into an existing mappings JSON string.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// Thrown when a field's <see cref="FieldContainer"/> intent contradicts the known parent type,
	/// e.g. <see cref="FieldContainer.Field"/> on an object/nested parent, or
	/// <see cref="FieldContainer.Property"/> on a leaf parent.
	/// </exception>
	public string MergeIntoMappings(string mappingsJson)
	{
		if (!HasConfiguration)
			return mappingsJson;

		var node = JsonNode.Parse(mappingsJson)?.AsObject() ?? [];

		// Merge field overrides into properties
		if (Fields.Count > 0)
		{
			var properties = node["properties"]?.AsObject();
			if (properties == null)
			{
				properties = [];
				node["properties"] = properties;
			}

			// Sort by depth ascending so parent fields are always merged before their children,
			// making explicit-intent merges order-independent regardless of declaration order.
			foreach (var kvp in Fields.OrderBy(f => f.Key.Count(c => c == '.')))
			{
				var container = FieldContainers.TryGetValue(kvp.Key, out var c) ? c : FieldContainer.Auto;
				MergeFieldAtPath(properties, kvp.Key, kvp.Value, container);
			}
		}

		// Merge runtime fields
		if (RuntimeFields.Count > 0)
		{
			var runtime = node["runtime"]?.AsObject();
			if (runtime == null)
			{
				runtime = [];
				node["runtime"] = runtime;
			}

			foreach (var kvp in RuntimeFields)
				runtime[kvp.Key] = kvp.Value.ToJson();
		}

		// Merge dynamic templates
		if (DynamicTemplates.Count > 0)
		{
			var templates = node["dynamic_templates"]?.AsArray();
			if (templates == null)
			{
				templates = [];
				node["dynamic_templates"] = templates;
			}

			foreach (var template in DynamicTemplates)
				templates.Add((JsonNode)template.ToJson());
		}

		return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
	}

	private static readonly HashSet<string> ObjectTypes = ["object", "nested"];

	private static void MergeFieldAtPath(JsonObject properties, string path, IFieldDefinition definition, FieldContainer intent)
	{
		var parts = path.Split('.');
		var current = properties;

		for (var i = 0; i < parts.Length - 1; i++)
		{
			var part = parts[i];
			var next = current[part]?.AsObject();
			var isTerminalParent = i == parts.Length - 2;

			if (next == null)
			{
				if (isTerminalParent && intent == FieldContainer.Field)
				{
					// Cannot attach a multi-field to a non-existent parent.
					// Silently creating type:object would be wrong and reproduce the bug.
					throw new InvalidOperationException(
						$"Cannot add '{parts[parts.Length - 1]}' as a multi-field of '{string.Join(".", parts, 0, parts.Length - 1)}': " +
						$"the parent field is not defined. " +
						$"Define the parent field first, or use AddProperty(\"{path}\", ...) if you intended a sub-property.");
				}

				// Property intent or intermediate segment: create an object parent.
				next = new JsonObject { ["type"] = "object" };
				current[part] = next;
			}

			var parentType = next["type"]?.GetValue<string>();
			var isObjectLike = parentType == null || ObjectTypes.Contains(parentType);

			if (isTerminalParent)
			{
				string containerKey;
				switch (intent)
				{
					case FieldContainer.Field:
						if (!isObjectLike)
						{
							// Leaf parent — correct for Field intent. Container = fields.
							containerKey = "fields";
						}
						else if (parentType != null)
						{
							// Known object/nested — contradiction.
							throw new InvalidOperationException(
								$"Cannot add '{parts[parts.Length - 1]}' as a multi-field of '{string.Join(".", parts, 0, parts.Length - 1)}' " +
								$"(type: {parentType}): object and nested fields use sub-properties. " +
								$"Use AddProperty(\"{path}\", ...) instead.");
						}
						else
						{
							// Parent exists but type is null/unknown — fall back to properties to avoid corruption.
							containerKey = "properties";
						}
						break;

					case FieldContainer.Property:
						if (isObjectLike)
						{
							// Object/nested parent — correct for Property intent. Container = properties.
							containerKey = "properties";
						}
						else
						{
							// Known leaf type — contradiction.
							throw new InvalidOperationException(
								$"Cannot add '{parts[parts.Length - 1]}' as a sub-property of '{string.Join(".", parts, 0, parts.Length - 1)}' " +
								$"(type: {parentType}): leaf fields use multi-fields. " +
								$"Use AddField(\"{path}\", ...) instead.");
						}
						break;

					default: // Auto — preserve today's inference
						containerKey = isObjectLike ? "properties" : "fields";
						break;
				}

				var container = next[containerKey]?.AsObject();
				if (container == null)
				{
					container = [];
					next[containerKey] = container;
				}
				current = container;
			}
			else
			{
				// Intermediate segment: use Auto inference unconditionally.
				var isLeafType = parentType != null && !ObjectTypes.Contains(parentType);
				var containerKey = isLeafType ? "fields" : "properties";
				var container = next[containerKey]?.AsObject();
				if (container == null)
				{
					container = [];
					next[containerKey] = container;
				}
				current = container;
			}
		}

		var fieldName = parts[parts.Length - 1];
		var existing = current[fieldName]?.AsObject();
		var newDef = definition.ToJson();

		if (existing != null)
		{
			foreach (var kvp in newDef)
				existing[kvp.Key] = kvp.Value?.DeepClone();
		}
		else
		{
			current[fieldName] = newDef;
		}
	}

	/// <summary>
	/// Creates an empty MappingOverrides instance.
	/// </summary>
	public static MappingOverrides Empty { get; } = new(
		new Dictionary<string, IFieldDefinition>(),
		new Dictionary<string, RuntimeFieldDefinition>(),
		[]
	);
}
