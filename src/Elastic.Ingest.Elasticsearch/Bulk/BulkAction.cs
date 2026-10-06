// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;

// ThrowIfNull is not available on netstandard
#pragma warning disable CA1510

namespace Elastic.Ingest.Elasticsearch.Bulk;

/// <summary>The kind of operation a <see cref="BulkAction"/> performs.</summary>
public enum BulkActionKind : byte
{
	/// <summary>Index (create or replace) the document.</summary>
	Index = 0,
	/// <summary>Create the document, failing with a 409 if it exists.</summary>
	Create,
	/// <summary>Update the document using <c>doc_as_upsert</c>.</summary>
	Update,
	/// <summary>Delete the document. No body line is written.</summary>
	Delete,
	/// <summary>Scripted upsert that only replaces the document when its hash changed.</summary>
	ScriptedHashUpsert
}

/// <summary>
/// The per document action line of a <c>_bulk</c> request.
/// <para>This is a small immutable struct: producing one per item does not allocate.</para>
/// </summary>
public readonly struct BulkAction
{
	private readonly object? _extra;

	private BulkAction(BulkActionKind kind, string? id, string? index, bool requireAlias, object? extra)
	{
		Kind = kind;
		Id = id;
		IndexName = index;
		RequireAlias = requireAlias;
		_extra = extra;
	}

	/// <summary>The operation to perform.</summary>
	public BulkActionKind Kind { get; }

	/// <summary>The document id, when <c>null</c> Elasticsearch generates one (index/create only).</summary>
	public string? Id { get; }

	/// <summary>The target index/data stream, when <c>null</c> the request level target is used.</summary>
	public string? IndexName { get; }

	/// <summary>Require <see cref="IndexName"/> to point to an alias.</summary>
	public bool RequireAlias { get; }

	/// <summary>Dynamic templates to apply (index/create only).</summary>
	public IReadOnlyDictionary<string, string>? DynamicTemplates => _extra as IReadOnlyDictionary<string, string>;

	/// <summary>The hash information when <see cref="Kind"/> is <see cref="BulkActionKind.ScriptedHashUpsert"/>.</summary>
	public HashedBulkUpdate? HashUpdate => _extra as HashedBulkUpdate;

	/// <summary>True if this action is followed by a document line.</summary>
	public bool HasBody => Kind != BulkActionKind.Delete;

	/// <summary>Index the document, replacing any existing document with the same id.</summary>
	public static BulkAction Index(string? id = null, string? index = null) =>
		new(BulkActionKind.Index, id, index, false, null);

	/// <summary>Create the document, failing with a 409 if it exists.</summary>
	public static BulkAction Create(string? id = null, string? index = null) =>
		new(BulkActionKind.Create, id, index, false, null);

	/// <summary>Upsert the document using <c>doc_as_upsert</c>.</summary>
	public static BulkAction Update(string id, string? index = null) =>
		new(BulkActionKind.Update, id, index, false, null);

	/// <summary>Delete the document. No body line is written.</summary>
	public static BulkAction Delete(string id, string? index = null) =>
		new(BulkActionKind.Delete, id, index, false, null);

	/// <summary>Scripted upsert that only replaces the document if <paramref name="update"/> hash differs from the stored one.</summary>
	public static BulkAction ScriptedHashUpsert(string id, HashedBulkUpdate update, string? index = null) =>
		new(BulkActionKind.ScriptedHashUpsert, id, index, false, update ?? throw new ArgumentNullException(nameof(update)));

	/// <summary>Returns a copy that requires the target to be an alias.</summary>
	public BulkAction WithRequireAlias() => new(Kind, Id, IndexName, true, _extra);

	/// <summary>Returns a copy with <paramref name="templates"/> as dynamic templates (index/create only).</summary>
	public BulkAction WithDynamicTemplates(IReadOnlyDictionary<string, string> templates) =>
		new(Kind, Id, IndexName, RequireAlias, templates);

	/// <summary>Converts a <see cref="BulkOperationHeader"/> into the equivalent <see cref="BulkAction"/>.</summary>
	public static BulkAction From(BulkOperationHeader header)
	{
		if (header is null) throw new ArgumentNullException(nameof(header));
		var action = header switch
		{
			CreateOperation c => Create(c.Id, c.Index).WithTemplatesIfAny(c.DynamicTemplates),
			IndexOperation i => Index(i.Id, i.Index).WithTemplatesIfAny(i.DynamicTemplates),
			DeleteOperation => Delete(header.Id!, header.Index),
			UpdateOperation => Update(header.Id!, header.Index),
			ScriptedHashUpdateOperation s => ScriptedHashUpsert(s.Id!, s.UpdateInformation, s.Index),
			_ => throw new ArgumentOutOfRangeException(nameof(header), header, null)
		};
		return header.RequireAlias == true ? action.WithRequireAlias() : action;
	}

	private BulkAction WithTemplatesIfAny(IReadOnlyDictionary<string, string>? templates) =>
		templates is null ? this : WithDynamicTemplates(templates);

	/// <summary>Converts a <see cref="BulkOperationHeader"/> into the equivalent <see cref="BulkAction"/>.</summary>
	public static implicit operator BulkAction(BulkOperationHeader header) => From(header);
}
