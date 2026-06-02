// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping.Mappings;

/// <summary>
/// Controls which container a dotted-path leaf field is merged into.
/// </summary>
public enum FieldContainer
{
	/// <summary>
	/// Infer from the parent field's type: multi-field (<c>fields</c>) for leaf types (text, keyword, etc.),
	/// sub-property (<c>properties</c>) for object/nested types.
	/// Used by generated property methods and nested builders to preserve existing inference behaviour.
	/// </summary>
	Auto,

	/// <summary>
	/// Always place the leaf under the parent's <c>"fields"</c> (multi-field).
	/// Only valid when the parent is a leaf type (text, keyword, date, etc.).
	/// Throws at merge time if the parent is an object or nested type — use <see cref="Property"/> instead.
	/// Throws if the parent is not defined at all (no base attribute and no sibling <c>AddField/AddProperty</c>
	/// call for the parent path).
	/// </summary>
	Field,

	/// <summary>
	/// Always place the leaf under the parent's <c>"properties"</c> (sub-property).
	/// Only valid when the parent is an object or nested type.
	/// Throws at merge time if the parent is a leaf type — use <see cref="Field"/> instead.
	/// Creates a new <c>type: object</c> parent when no parent is defined yet.
	/// </summary>
	Property
}
