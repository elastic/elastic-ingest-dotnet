// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
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
	/// Set by the bootstrap process before steps run.
	/// </summary>
	public string ChannelHash { get; set; } = string.Empty;

	/// <summary> Optional ILM policy name. </summary>
	public string? IlmPolicy { get; init; }

	/// <summary> Whether the Elasticsearch instance is serverless. Lazily detected. </summary>
	public bool IsServerless { get; set; }

	/// <summary> The template name for bootstrap. </summary>
	public required string TemplateName { get; init; }

	/// <summary> The template wildcard pattern for bootstrap. </summary>
	public required string TemplateWildcard { get; init; }

	/// <summary>
	/// Function to get the settings JSON for the component template.
	/// </summary>
	public Func<string>? GetSettingsJson { get; init; }

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
	/// Additional properties that steps may use. Allows extensibility without modifying the context.
	/// </summary>
	public Dictionary<string, object>? Properties { get; set; }
}
