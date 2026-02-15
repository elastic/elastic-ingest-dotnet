// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Shared context passed to bootstrap steps. Steps derive template names, JSON, etc.
/// from the TypeContext.
/// </summary>
public class BootstrapContext
{
	/// <summary> The transport to use for Elasticsearch API calls. </summary>
	public required ITransport Transport { get; init; }

	/// <summary> The bootstrap method controlling error handling. </summary>
	public BootstrapMethod BootstrapMethod { get; init; }

	/// <summary>
	/// The hash of the channel's settings and mappings.
	/// Set by the bootstrap process (e.g. <c>ComponentTemplateStep</c>) before template steps run.
	/// </summary>
	public string ChannelHash { get; internal set; } = string.Empty;

	/// <summary> Whether the Elasticsearch instance is serverless. Lazily detected. </summary>
	public bool IsServerless { get; internal set; }

	/// <summary> The template name for bootstrap. </summary>
	public required string TemplateName { get; init; }

	/// <summary> The template wildcard pattern for bootstrap. </summary>
	public required string TemplateWildcard { get; init; }

	/// <summary>
	/// Function to get the mappings JSON for the component template.
	/// </summary>
	public Func<string>? GetMappingsJson { get; init; }

	/// <summary>
	/// Function to get settings that accompany mappings (analysis settings, etc.).
	/// </summary>
	public Func<string>? GetMappingSettings { get; init; }

	/// <summary>
	/// Data stream type (e.g. "logs", "metrics") for inferring built-in component templates.
	/// </summary>
	public string? DataStreamType { get; init; }

	/// <summary>
	/// Data stream lifecycle retention period (e.g. "30d").
	/// Set by <c>DataStreamLifecycleStep</c> for <c>DataStreamTemplateStep</c> to embed.
	/// </summary>
	public string? DataStreamLifecycleRetention { get; internal set; }
}
