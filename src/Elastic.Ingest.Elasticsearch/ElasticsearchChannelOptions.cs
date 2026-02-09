// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using Elastic.Ingest.Elasticsearch.Strategies;
using Elastic.Mapping;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// Options for the composable <see cref="ElasticsearchChannel{TEvent}"/>.
/// Provide an <see cref="ElasticsearchTypeContext"/> for automatic strategy configuration,
/// or configure strategies manually.
/// </summary>
public class ElasticsearchChannelOptions<TEvent> : ElasticsearchChannelOptionsBase<TEvent>
	where TEvent : class
{
	/// <summary>
	/// Creates options with an <see cref="ElasticsearchTypeContext"/> for automatic configuration.
	/// Strategies, template names, and mappings are auto-derived from the TypeContext.
	/// </summary>
	public ElasticsearchChannelOptions(ITransport transport, ElasticsearchTypeContext typeContext) : base(transport) =>
		TypeContext = typeContext;

	/// <summary>
	/// Creates options for manual strategy configuration.
	/// You must set <see cref="IngestStrategy"/>, <see cref="TemplateName"/>, and <see cref="TemplateWildcard"/>.
	/// </summary>
	public ElasticsearchChannelOptions(ITransport transport) : base(transport) { }

	/// <summary>
	/// The source-generated type context providing mappings, settings, and accessor delegates.
	/// When set, strategies are auto-configured from this context unless explicitly overridden.
	/// </summary>
	public ElasticsearchTypeContext? TypeContext { get; }

	/// <summary>
	/// Per-document ingest strategy controlling bulk operation headers, URL, and refresh targets.
	/// Auto-configured from <see cref="TypeContext"/> when not set.
	/// </summary>
	public IDocumentIngestStrategy<TEvent>? IngestStrategy { get; set; }

	/// <summary>
	/// The index template name for bootstrap.
	/// Auto-derived from <see cref="TypeContext"/> when not set.
	/// </summary>
	public string? TemplateName { get; set; }

	/// <summary>
	/// The index template wildcard pattern for bootstrap.
	/// Auto-derived from <see cref="TypeContext"/> when not set.
	/// </summary>
	public string? TemplateWildcard { get; set; }

	/// <summary>
	/// Bootstrap strategy controlling index/component template creation.
	/// Auto-configured based on <see cref="ElasticsearchTypeContext.EntityTarget"/> when not set.
	/// </summary>
	public IBootstrapStrategy? BootstrapStrategy { get; set; }

	/// <summary>
	/// Provisioning strategy controlling whether to create new or reuse existing indices.
	/// Defaults to always creating new. Auto-upgraded to hash-based reuse when
	/// <see cref="ElasticsearchTypeContext.GetContentHash"/> is available.
	/// </summary>
	public IIndexProvisioningStrategy? ProvisioningStrategy { get; set; }

	/// <summary>
	/// Alias strategy controlling alias management after indexing.
	/// Auto-configured from <see cref="ElasticsearchTypeContext.SearchStrategy"/> when not set.
	/// </summary>
	public IAliasStrategy? AliasStrategy { get; set; }

	/// <summary>
	/// Function to get mappings JSON for bootstrap component templates.
	/// Auto-derived from <see cref="ElasticsearchTypeContext.GetMappingsJson"/> when not set.
	/// </summary>
	public Func<string>? GetMappingsJson { get; set; }

	/// <summary>
	/// Function to get settings that accompany mappings (analysis settings, etc.).
	/// Auto-derived from <see cref="ElasticsearchTypeContext.GetSettingsJson"/> when not set.
	/// </summary>
	public Func<string>? GetMappingSettings { get; set; }

	/// <summary> Optional ILM policy name. </summary>
	public string? IlmPolicy { get; set; }

	/// <summary> Data stream type (e.g. "logs", "metrics") for inferring built-in component templates. </summary>
	public string? DataStreamType { get; set; }
}
