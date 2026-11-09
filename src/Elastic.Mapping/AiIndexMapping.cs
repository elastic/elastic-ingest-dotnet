// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Mappings;

namespace Elastic.Mapping;

/// <summary>
/// The base mapping Elasticsearch ships for AI indices (<c>ai-index@mappings</c>, Elasticsearch 9.6+),
/// and the helpers that fold it into a document type's own mapping.
/// <para>
/// Setting <c>AiIndex = true</c> on <c>[Index&lt;T&gt;]</c> or <c>[DataStream&lt;T&gt;]</c> adds these fields
/// to the generated mapping <em>additively</em>: every field the document type already maps keeps its
/// definition, and a field that is only partly present (for example a <c>title</c> that has no
/// <c>.semantic</c> multi-field) gains the missing parts.
/// </para>
/// </summary>
public static class AiIndexMapping
{
	/// <summary>The component template Elasticsearch registers with the AI index base mappings.</summary>
	public const string MappingsComponentTemplate = "ai-index@mappings";

	/// <summary>The optional component template customers use to extend AI indices.</summary>
	public const string CustomComponentTemplate = "ai-index@custom";

	/// <summary>
	/// Index template priority used for AI index templates. Elasticsearch's own <c>ai-index-*</c>
	/// templates use 500, and only the highest priority template that matches an index applies,
	/// so a template that must apply to such an index has to be higher.
	/// </summary>
	public const int TemplatePriority = 501;

	/// <summary>
	/// Embedded copy of <c>ai-index@mappings</c> (registry version 4), as a mappings body.
	/// It keeps the generated mapping complete on clusters that do not have the component template.
	/// </summary>
	public const string BaseMappingJson = """
		{
		  "properties": {
		    "@timestamp": { "type": "date" },
		    "id": { "type": "keyword" },
		    "type": { "type": "keyword", "normalizer": "lowercase" },
		    "title": {
		      "type": "text",
		      "fields": { "semantic": { "type": "semantic_text" } }
		    },
		    "description": {
		      "type": "text",
		      "fields": { "semantic": { "type": "semantic_text" } }
		    },
		    "content": {
		      "type": "text",
		      "fields": { "semantic": { "type": "semantic_text" } }
		    },
		    "tags": { "type": "keyword", "normalizer": "lowercase" },
		    "attributes": { "type": "flattened" },
		    "updated_at": { "type": "date" },
		    "expires_at": { "type": "date" },
		    "references": {
		      "type": "object",
		      "properties": {
		        "uri": { "type": "keyword" },
		        "relation": { "type": "keyword" },
		        "description": { "type": "text" }
		      }
		    },
		    "governance": {
		      "type": "object",
		      "properties": {
		        "provenance": {
		          "type": "object",
		          "properties": {
		            "created_by": {
		              "type": "object",
		              "properties": {
		                "uri": { "type": "keyword" },
		                "metadata": { "type": "flattened" }
		              }
		            },
		            "updated_by": {
		              "type": "object",
		              "properties": {
		                "uri": { "type": "keyword" },
		                "metadata": { "type": "flattened" }
		              }
		            }
		          }
		        },
		        "lifecycle": {
		          "type": "object",
		          "properties": {
		            "status": { "type": "keyword" }
		          }
		        }
		      }
		    }
		  }
		}
		""";

	/// <summary>
	/// Adds the AI index base fields to <paramref name="mappingsJson"/>. Fields the mapping already
	/// defines are never changed; only missing fields, and missing multi-fields or sub-properties of
	/// existing fields, are added.
	/// </summary>
	public static string MergeInto(string mappingsJson) =>
		new MappingOverrides(
			new Dictionary<string, Mappings.Definitions.IFieldDefinition>(),
			new Dictionary<string, Mappings.Definitions.RuntimeFieldDefinition>(),
			[],
			mergeSources: [BaseMappingJson]
		).MergeIntoMappings(mappingsJson);
}
