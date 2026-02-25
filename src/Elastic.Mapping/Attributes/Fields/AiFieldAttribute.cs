// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Marks a property on a document type as an AI-generated output field.
/// The description is included in the JSON schema sent to the LLM.
/// Supported property types: <c>string</c> and <c>string[]</c>.
/// <para>
/// Each field gets its own prompt hash (SHA-256 of its description) stored in the
/// lookup index, enabling granular cache invalidation — changing one field's description
/// only regenerates that field, not all fields.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class AiFieldAttribute : Attribute
{
	/// <inheritdoc cref="AiFieldAttribute"/>
	public AiFieldAttribute(string description) => Description = description;

	/// <summary>
	/// Description of what this field should contain. Included verbatim in the
	/// JSON schema sent to the LLM as the <c>"description"</c> property.
	/// </summary>
	public string Description { get; }

	/// <summary>
	/// Minimum number of items for <c>string[]</c> properties.
	/// Emitted as <c>"minItems"</c> in the JSON schema. Ignored for <c>string</c> properties.
	/// </summary>
	public int MinItems { get; init; }

	/// <summary>
	/// Maximum number of items for <c>string[]</c> properties.
	/// Emitted as <c>"maxItems"</c> in the JSON schema. Ignored for <c>string</c> properties.
	/// </summary>
	public int MaxItems { get; init; }
}
