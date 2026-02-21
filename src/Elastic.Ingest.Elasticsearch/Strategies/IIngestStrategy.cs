// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Composed strategy that defines all behaviors for an <see cref="IngestChannel{TEvent}"/>.
/// Use <see cref="IngestStrategies"/> factory methods to create common configurations,
/// or implement directly for full control.
/// </summary>
public interface IIngestStrategy<TEvent> where TEvent : class
{
	/// <summary> Bootstrap strategy controlling index/component template creation. </summary>
	IBootstrapStrategy Bootstrap { get; }

	/// <summary> Per-document ingest strategy controlling bulk operation headers, URL, and refresh targets. </summary>
	IDocumentIngestStrategy<TEvent> DocumentIngest { get; }

	/// <summary> Provisioning strategy controlling whether to create new or reuse existing indices. </summary>
	IIndexProvisioningStrategy Provisioning { get; }

	/// <summary> Alias strategy controlling alias management after indexing. </summary>
	IAliasStrategy AliasStrategy { get; }

	/// <summary> Optional rollover strategy for manually rolling over indices or data streams. </summary>
	IRolloverStrategy? Rollover { get; }

	/// <summary> The index template name for bootstrap. </summary>
	string TemplateName { get; }

	/// <summary> The index template wildcard pattern for bootstrap. </summary>
	string TemplateWildcard { get; }

	/// <summary> Function to get mappings JSON for bootstrap component templates. </summary>
	Func<string>? GetMappingsJson { get; }

	/// <summary> Function to get settings that accompany mappings (analysis settings, etc.). </summary>
	Func<string>? GetMappingSettings { get; }

	/// <summary> Data stream type (e.g. "logs", "metrics") for inferring built-in component templates. </summary>
	string? DataStreamType { get; }

	/// <summary>
	/// Additional index settings to include in the settings component template
	/// (e.g. <c>index.default_pipeline</c>).
	/// </summary>
	IReadOnlyDictionary<string, string>? AdditionalSettings { get; }
}
