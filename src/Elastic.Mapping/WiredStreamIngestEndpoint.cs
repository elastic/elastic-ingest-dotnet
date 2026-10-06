// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Wired stream bulk ingest endpoint. Controls the URL prefix used by
/// <c>WiredStreamIngestStrategy</c> (e.g. <c>logs/_bulk</c>, <c>logs.ecs/_bulk</c>).
/// </summary>
/// <remarks>
/// See <see href="https://www.elastic.co/docs/solutions/observability/streams/wired-streams-field-naming">
/// Wired streams field naming</see> for how <see cref="LogsEcs"/> and <see cref="LogsOtel"/>
/// normalize field names.
/// </remarks>
public enum WiredStreamIngestEndpoint
{
	/// <summary>
	/// Legacy/generic wired logs endpoint (<c>logs/_bulk</c>).
	/// Default for backward compatibility.
	/// </summary>
	Logs,

	/// <summary>
	/// ECS field naming endpoint (<c>logs.ecs/_bulk</c>).
	/// Prefer this when sending Elastic Common Schema documents (e.g. from ecs-dotnet).
	/// </summary>
	LogsEcs,

	/// <summary>
	/// OpenTelemetry semantic convention endpoint (<c>logs.otel/_bulk</c>).
	/// Converts ECS fields to OTel equivalents; creates ECS aliases for queries.
	/// </summary>
	LogsOtel
}

/// <summary>
/// Helpers for <see cref="WiredStreamIngestEndpoint"/>.
/// </summary>
public static class WiredStreamIngestEndpointExtensions
{
	/// <summary>
	/// Returns the bulk URL path prefix for the endpoint (without trailing slash).
	/// </summary>
	public static string ToBulkPathPrefix(this WiredStreamIngestEndpoint endpoint) =>
		endpoint switch
		{
			WiredStreamIngestEndpoint.LogsEcs => "logs.ecs",
			WiredStreamIngestEndpoint.LogsOtel => "logs.otel",
			_ => "logs"
		};
}
