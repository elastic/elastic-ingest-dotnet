// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping.Generator.Model;

/// <summary>
/// An equatable (string/value-type only) record of a detected misuse of
/// <c>AddField</c> or <c>AddProperty</c> inside a <c>ConfigureMappings</c> method.
/// Carried through the incremental generator pipeline without breaking caching.
/// </summary>
internal sealed record MappingMisuseFinding(
	/// <summary>Diagnostic ID: EMAP001 or EMAP002.</summary>
	string DiagnosticId,
	/// <summary>Parent field name (the first path segment).</summary>
	string ParentFieldName,
	/// <summary>Full dotted path as written in the source (e.g. "title.semantic_text").</summary>
	string FullPath,
	/// <summary>Leaf segment (e.g. "semantic_text").</summary>
	string ChildSegment,
	/// <summary>Elasticsearch field type of the parent (e.g. "text", "object").</summary>
	string ParentFieldType,
	/// <summary>Source file path for reconstructing the diagnostic location.</summary>
	string FilePath,
	/// <summary>Start offset of the string-literal argument in the source file.</summary>
	int SpanStart,
	/// <summary>Length of the string-literal argument in the source file.</summary>
	int SpanLength
);
