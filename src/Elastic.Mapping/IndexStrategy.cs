// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Contains information about where to write documents.
/// Pure data bag — all resolve logic lives on <see cref="ElasticsearchTypeContext"/>.
/// </summary>
public sealed class IndexStrategy
{
	/// <summary>The target to write to (alias or index base name).</summary>
	public string? WriteTarget { get; init; }

	/// <summary>Date pattern for rolling indices (e.g., <c>"yyyy.MM.dd"</c>).</summary>
	public string? DatePattern { get; init; }

	/// <summary>Full data stream name if using data streams.</summary>
	public string? DataStreamName { get; init; }

	/// <summary>Data stream type component.</summary>
	public string? Type { get; init; }

	/// <summary>Data stream dataset component.</summary>
	public string? Dataset { get; init; }

	/// <summary>Data stream namespace component.</summary>
	public string? Namespace { get; init; }
}
