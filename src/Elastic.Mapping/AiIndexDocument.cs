// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Serialization;

namespace Elastic.Mapping;

/// <summary>
/// Base class for documents of an Elasticsearch AI index: it carries the root fields of
/// <c>ai-index@mappings</c> (see <see cref="AiIndexMapping"/>) with their JSON names and field types.
/// <para>
/// Derive from it, add your own fields, and register the type with <c>AiIndex = true</c>:
/// </para>
/// <code>
/// public class Article : AiIndexDocument
/// {
///     public string Author { get; set; }
/// }
///
/// [ElasticsearchMappingContext]
/// [Index&lt;Article&gt;(Name = "ai-index-idx-articles", AiIndex = true)]
/// public static partial class ArticleContext;
/// </code>
/// <para>
/// The class declares no roles apart from <see cref="IdAttribute"/> on <see cref="Id"/>. A derived type
/// adds the roles its channel needs, either on its own properties or by overriding a property of this
/// class: <c>[Timestamp]</c> for a data stream, <c>[LastUpdated]</c> for incremental sync, and so on.
/// A property with the same name in a derived type wins, so overriding a property also changes its
/// attributes, for example to give <see cref="Title"/> an analyzer.
/// </para>
/// <para>
/// <c>AiIndex = true</c> adds the <c>.semantic</c> multi-fields of <see cref="Title"/>,
/// <see cref="Description"/> and <see cref="Content"/>. Without it the type still maps every
/// base field, but not those.
/// </para>
/// </summary>
public abstract class AiIndexDocument
{
	/// <summary>
	/// When this document was created. For a data stream, the write time of this version of the document.
	/// Defaults to the time the instance is created.
	/// </summary>
	[JsonPropertyName("@timestamp")]
	public virtual DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

	/// <summary>
	/// The stable identity of the document. On an index it equals <c>_id</c>; on a data stream it is a
	/// logical key and each write is a new document.
	/// </summary>
	[Id]
	[Keyword]
	[JsonPropertyName("id")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public virtual string? Id { get; set; }

	/// <summary>
	/// What the document is. A free keyword; dotted sub-types such as <c>security.technology</c> are a
	/// naming convention. Lowercased by a normalizer.
	/// </summary>
	[Keyword(Normalizer = "lowercase")]
	[JsonPropertyName("type")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public virtual string? Type { get; set; }

	/// <summary>A short name. Lexical and, with <c>AiIndex = true</c>, semantic search.</summary>
	[JsonPropertyName("title")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public virtual string? Title { get; set; }

	/// <summary>A summary. Lexical and, with <c>AiIndex = true</c>, semantic search.</summary>
	[JsonPropertyName("description")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public virtual string? Description { get; set; }

	/// <summary>The body. Lexical and, with <c>AiIndex = true</c>, semantic search.</summary>
	[JsonPropertyName("content")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public virtual string? Content { get; set; }

	/// <summary>Filter facets. Lowercased by a normalizer.</summary>
	[Keyword(Normalizer = "lowercase")]
	[JsonPropertyName("tags")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public virtual string[]? Tags { get; set; }

	/// <summary>Free-form metadata, indexed as a <c>flattened</c> field: leaf values are keywords.</summary>
	[Flattened]
	[JsonPropertyName("attributes")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public virtual Dictionary<string, object?>? Attributes { get; set; }

	/// <summary>When the document was last modified.</summary>
	[JsonPropertyName("updated_at")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public virtual DateTimeOffset? UpdatedAt { get; set; }

	/// <summary>When the document drops out of retrieval. A document without a value never expires.</summary>
	[JsonPropertyName("expires_at")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public virtual DateTimeOffset? ExpiresAt { get; set; }

	/// <summary>Related URIs, outside of provenance.</summary>
	[Object]
	[JsonPropertyName("references")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public virtual AiIndexReference[]? References { get; set; }

	/// <summary>Who made the document and whether it is still wanted.</summary>
	[Object]
	[JsonPropertyName("governance")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public virtual AiIndexGovernance? Governance { get; set; }

	/// <summary>
	/// Records <paramref name="actor"/> as the mechanism that wrote this version of the document, at
	/// <paramref name="at"/>: it sets <c>governance.provenance.updated_by</c> and <see cref="UpdatedAt"/>, and
	/// <c>governance.provenance.created_by</c> when that is empty. Other governance fields are left as they are.
	/// <para>
	/// <paramref name="actor"/> is stored as given, not copied, so one instance can serve many documents;
	/// do not change it afterwards.
	/// </para>
	/// <para>
	/// The sync orchestrators call this for every document they write when their <c>Provenance</c> option is set.
	/// </para>
	/// </summary>
	public void StampProvenance(AiIndexActor actor, DateTimeOffset at)
	{
		var governance = Governance ??= new AiIndexGovernance();
		var provenance = governance.Provenance ??= new AiIndexProvenance();
		provenance.CreatedBy ??= actor;
		provenance.UpdatedBy = actor;
		UpdatedAt = at;
	}
}

/// <summary>A URI related to an AI index document, and how it relates.</summary>
public class AiIndexReference
{
	/// <summary>The URI, for example <c>https://example.com/page</c> or <c>ki://index/id</c>.</summary>
	[Keyword]
	[JsonPropertyName("uri")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Uri { get; set; }

	/// <summary>How the URI relates to the document, for example <c>derived_from</c> or <c>supersedes</c>.</summary>
	[Keyword]
	[JsonPropertyName("relation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Relation { get; set; }

	/// <summary>Why the reference exists, for progressive disclosure to a language model.</summary>
	[JsonPropertyName("description")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Description { get; set; }
}

/// <summary>Provenance and lifecycle of an AI index document. Kept apart so it is easy to leave out of a model's context.</summary>
public class AiIndexGovernance
{
	/// <summary>Who created and last updated the document.</summary>
	[Object]
	[JsonPropertyName("provenance")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public AiIndexProvenance? Provenance { get; set; }

	/// <summary>Whether the document is still wanted.</summary>
	[Object]
	[JsonPropertyName("lifecycle")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public AiIndexLifecycle? Lifecycle { get; set; }
}

/// <summary>Who created and last updated an AI index document.</summary>
public class AiIndexProvenance
{
	/// <summary>The mechanism that created the document.</summary>
	[Object]
	[JsonPropertyName("created_by")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public AiIndexActor? CreatedBy { get; set; }

	/// <summary>The mechanism that last updated the document.</summary>
	[Object]
	[JsonPropertyName("updated_by")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public AiIndexActor? UpdatedBy { get; set; }
}

/// <summary>A mechanism that wrote an AI index document, such as a workflow, an agent or a person.</summary>
public class AiIndexActor
{
	/// <summary>The URI of the mechanism, for example <c>workflow://id</c>.</summary>
	[Keyword]
	[JsonPropertyName("uri")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Uri { get; set; }

	/// <summary>More about the mechanism, such as a version or a run id.</summary>
	[Flattened]
	[JsonPropertyName("metadata")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Dictionary<string, object?>? Metadata { get; set; }
}

/// <summary>Whether an AI index document is still wanted.</summary>
public class AiIndexLifecycle
{
	/// <summary>
	/// <c>active</c> or <c>deleted</c>. A missing value reads as <c>active</c>; a <c>deleted</c> document
	/// is a tombstone that stays in the index.
	/// </summary>
	[Keyword]
	[JsonPropertyName("status")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? Status { get; set; }
}
