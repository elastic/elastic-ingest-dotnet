// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Configures a date field. DateTime properties are auto-inferred as date,
/// so this is only needed to customize format or other options.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DateAttribute : Attribute
{
	/// <summary>Date format (e.g., "strict_date_optional_time", "epoch_millis").</summary>
	public string? Format { get; init; }

	/// <summary>Whether to store doc values for sorting/aggregations. Set to false to disable (default: true).</summary>
	public bool DocValues { get; init; } = true;

	/// <summary>Whether the field is searchable. Set to false to disable (default: true).</summary>
	public bool Index { get; init; } = true;
}
