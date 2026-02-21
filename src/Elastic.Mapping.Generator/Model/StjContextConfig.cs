// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping.Generator.Model;

/// <summary>
/// Configuration extracted from a <c>[JsonSourceGenerationOptions]</c> attribute
/// on a linked <c>JsonSerializerContext</c>.
/// </summary>
internal sealed record StjContextConfig(
	NamingPolicy PropertyNamingPolicy,
	bool UseStringEnumConverter,
	DefaultIgnoreCondition IgnoreCondition,
	bool IgnoreReadOnlyProperties
)
{
	public static StjContextConfig Default { get; } = new(
		NamingPolicy.Unspecified,
		false,
		DefaultIgnoreCondition.Never,
		false
	);
}

/// <summary>
/// Mirrors <c>System.Text.Json.JsonKnownNamingPolicy</c> values.
/// The generator targets netstandard2.0 and cannot reference STJ enums directly.
/// </summary>
internal enum NamingPolicy
{
	Unspecified = 0,
	CamelCase = 1,
	SnakeCaseLower = 2,
	SnakeCaseUpper = 3,
	KebabCaseLower = 4,
	KebabCaseUpper = 5
}

/// <summary>
/// Mirrors <c>System.Text.Json.Serialization.JsonIgnoreCondition</c> values.
/// </summary>
internal enum DefaultIgnoreCondition
{
	Never = 0,
	Always = 1,
	WhenWritingDefault = 2,
	WhenWritingNull = 3
}
