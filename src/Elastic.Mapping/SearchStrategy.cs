// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Contains information about where to read/search documents.
/// </summary>
public sealed class SearchStrategy
{
	/// <summary>Search pattern for queries (e.g., "logs-*").</summary>
	public string? Pattern { get; init; }

	/// <summary>Read alias for ILM (e.g., "logs-read").</summary>
	public string? ReadAlias { get; init; }

	/// <summary>
	/// Gets the effective search target, preferring pattern over alias.
	/// </summary>
	public string GetSearchTarget() =>
		Pattern ?? ReadAlias ?? throw new InvalidOperationException("No search target configured");
}
