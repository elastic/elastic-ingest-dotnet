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

	internal MappingOverrides(
		IReadOnlyDictionary<string, IFieldDefinition> fields,
		IReadOnlyDictionary<string, RuntimeFieldDefinition> runtimeFields,
		IReadOnlyList<DynamicTemplateDefinition> dynamicTemplates)
	{
		Fields = fields;
		RuntimeFields = runtimeFields;
		DynamicTemplates = dynamicTemplates;
	}

	/// <summary>Returns true if any mapping overrides are configured.</summary>
	public bool HasConfiguration =>
		Fields.Count > 0 ||
		RuntimeFields.Count > 0 ||
		DynamicTemplates.Count > 0;

	/// <summary>
	/// Merges these mapping overrides into an existing mappings JSON string.
	/// </summary>
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

			foreach (var kvp in Fields)
				MergeFieldAtPath(properties, kvp.Key, kvp.Value);
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
				templates.Add(template.ToJson());
		}

		return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
	}

	private static void MergeFieldAtPath(JsonObject properties, string path, IFieldDefinition definition)
	{
		var parts = path.Split('.');
		var current = properties;

		// Navigate to the nested location, creating parent objects as needed
		for (var i = 0; i < parts.Length - 1; i++)
		{
			var part = parts[i];
			var next = current[part]?.AsObject();

			if (next == null)
			{
				next = new JsonObject { ["type"] = "object" };
				current[part] = next;
			}

			var nextProps = next["properties"]?.AsObject();
			if (nextProps == null)
			{
				nextProps = [];
				next["properties"] = nextProps;
			}

			current = nextProps;
		}

		// Set the field definition at the final location
		var fieldName = parts[^1];
		var existing = current[fieldName]?.AsObject();
		var newDef = definition.ToJson();

		if (existing != null)
		{
			// Merge new definition into existing
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
