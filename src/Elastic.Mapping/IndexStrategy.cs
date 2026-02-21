// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Globalization;

namespace Elastic.Mapping;

/// <summary>
/// Contains information about where to write documents.
/// </summary>
public sealed class IndexStrategy
{
	/// <summary>The target to write to (alias or data stream name).</summary>
	public string? WriteTarget { get; init; }

	/// <summary>Date pattern for rolling indices (e.g., "yyyy.MM.dd").</summary>
	public string? DatePattern { get; init; }

	/// <summary>Full data stream name if using data streams.</summary>
	public string? DataStreamName { get; init; }

	/// <summary>Data stream type component.</summary>
	public string? Type { get; init; }

	/// <summary>Data stream dataset component.</summary>
	public string? Dataset { get; init; }

	/// <summary>Data stream namespace component.</summary>
	public string? Namespace { get; init; }

	/// <summary>
	/// Gets the effective write target, resolving data stream or alias.
	/// When <see cref="DataStreamName"/> is null but <see cref="Type"/> and <see cref="Dataset"/>
	/// are set, resolves the namespace from environment variables via
	/// <see cref="ElasticsearchTypeContext.ResolveDefaultNamespace"/>.
	/// </summary>
	public string GetWriteTarget()
	{
		if (DataStreamName != null)
			return DataStreamName;

		if (Type != null && Dataset != null)
		{
			var ns = Namespace ?? ElasticsearchTypeContext.ResolveDefaultNamespace();
			return $"{Type}-{Dataset}-{ns}";
		}

		return WriteTarget ?? throw new InvalidOperationException("No write target configured");
	}

	/// <summary>
	/// Gets the write target with optional date formatting.
	/// </summary>
	public string GetWriteTarget(DateTime date) =>
		DatePattern != null && WriteTarget != null
			? $"{WriteTarget}-{date.ToString(DatePattern, CultureInfo.InvariantCulture)}"
			: GetWriteTarget();
}
