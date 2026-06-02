// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Microsoft.CodeAnalysis;

namespace Elastic.Mapping.Generator.Diagnostics;

internal static class MappingDiagnostics
{
	/// <summary>
	/// EMAP001: <c>AddField</c> used on a property whose type is object or nested.
	/// The developer should use <c>AddProperty</c> instead.
	/// </summary>
	public static readonly DiagnosticDescriptor AddFieldOnObjectParent = new(
		"EMAP001",
		"Use AddProperty for object/nested sub-properties",
		"'{0}' is an object/nested field. Use AddProperty(\"{1}\", ...) instead of AddField to add sub-property '{2}'.",
		"Elastic.Mapping",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);

	/// <summary>
	/// EMAP002: <c>AddProperty</c> used on a property whose type is a leaf (text, keyword, etc.).
	/// The developer should use <c>AddField</c> instead.
	/// </summary>
	public static readonly DiagnosticDescriptor AddPropertyOnLeafParent = new(
		"EMAP002",
		"Use AddField for multi-fields on leaf fields",
		"'{0}' is a '{3}' (leaf) field. Use AddField(\"{1}\", ...) instead of AddProperty to add multi-field '{2}'.",
		"Elastic.Mapping",
		DiagnosticSeverity.Error,
		isEnabledByDefault: true);
}
