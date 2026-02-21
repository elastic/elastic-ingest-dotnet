// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using Elastic.Mapping;
using Elastic.Mapping.Analysis;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Default implementation of <see cref="IIngestStrategy{TEvent}"/> that bundles all sub-strategies.
/// Use <see cref="IngestStrategies"/> factory methods to create common configurations.
/// </summary>
public class IngestStrategy<TEvent> : IIngestStrategy<TEvent>
	where TEvent : class
{
	/// <summary>
	/// Creates a composed ingest strategy from individual sub-strategies and a type context.
	/// </summary>
	public IngestStrategy(
		ElasticsearchTypeContext typeContext,
		IBootstrapStrategy bootstrap,
		IDocumentIngestStrategy<TEvent> documentIngest,
		IIndexProvisioningStrategy provisioning,
		IAliasStrategy alias,
		IRolloverStrategy? rollover = null,
		IReadOnlyDictionary<string, string>? additionalSettings = null)
	{
		Bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
		DocumentIngest = documentIngest ?? throw new ArgumentNullException(nameof(documentIngest));
		Provisioning = provisioning ?? throw new ArgumentNullException(nameof(provisioning));
		AliasStrategy = alias ?? throw new ArgumentNullException(nameof(alias));
		Rollover = rollover;
		AdditionalSettings = additionalSettings;

		TemplateName = IngestStrategies.ResolveTemplateName(typeContext);
		TemplateWildcard = IngestStrategies.ResolveTemplateWildcard(typeContext);
		GetMappingsJson = typeContext.GetMappingsJson;
		GetMappingSettings = BuildMergedSettings(typeContext);
		DataStreamType = typeContext.IndexStrategy?.Type;
	}

	/// <summary>
	/// Creates a composed ingest strategy with explicit template configuration.
	/// </summary>
	public IngestStrategy(
		string templateName,
		string templateWildcard,
		IBootstrapStrategy bootstrap,
		IDocumentIngestStrategy<TEvent> documentIngest,
		IIndexProvisioningStrategy provisioning,
		IAliasStrategy alias,
		Func<string>? getMappingsJson = null,
		Func<string>? getMappingSettings = null,
		string? dataStreamType = null,
		IRolloverStrategy? rollover = null,
		IReadOnlyDictionary<string, string>? additionalSettings = null)
	{
		TemplateName = templateName ?? throw new ArgumentNullException(nameof(templateName));
		TemplateWildcard = templateWildcard ?? throw new ArgumentNullException(nameof(templateWildcard));
		Bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
		DocumentIngest = documentIngest ?? throw new ArgumentNullException(nameof(documentIngest));
		Provisioning = provisioning ?? throw new ArgumentNullException(nameof(provisioning));
		AliasStrategy = alias ?? throw new ArgumentNullException(nameof(alias));
		GetMappingsJson = getMappingsJson;
		GetMappingSettings = getMappingSettings;
		DataStreamType = dataStreamType;
		Rollover = rollover;
		AdditionalSettings = additionalSettings;
	}

	/// <inheritdoc />
	public IBootstrapStrategy Bootstrap { get; }

	/// <inheritdoc />
	public IDocumentIngestStrategy<TEvent> DocumentIngest { get; }

	/// <inheritdoc />
	public IIndexProvisioningStrategy Provisioning { get; }

	/// <inheritdoc />
	public IAliasStrategy AliasStrategy { get; }

	/// <inheritdoc />
	public IRolloverStrategy? Rollover { get; }

	/// <inheritdoc />
	public string TemplateName { get; }

	/// <inheritdoc />
	public string TemplateWildcard { get; }

	/// <inheritdoc />
	public Func<string>? GetMappingsJson { get; }

	/// <inheritdoc />
	public Func<string>? GetMappingSettings { get; }

	/// <inheritdoc />
	public string? DataStreamType { get; }

	/// <inheritdoc />
	public IReadOnlyDictionary<string, string>? AdditionalSettings { get; }

	/// <summary>
	/// Merges the analysis settings from <see cref="ElasticsearchTypeContext.ConfigureAnalysis"/>
	/// into the base settings JSON from <see cref="ElasticsearchTypeContext.GetSettingsJson"/>.
	/// </summary>
	private static Func<string>? BuildMergedSettings(ElasticsearchTypeContext tc)
	{
		if (tc.GetSettingsJson == null && tc.ConfigureAnalysis == null)
			return null;

		return () =>
		{
			var baseJson = tc.GetSettingsJson?.Invoke();
			string? analysisJson = null;

			if (tc.ConfigureAnalysis != null)
			{
				var builder = new AnalysisBuilder();
				var result = tc.ConfigureAnalysis(builder);
				if (result.HasConfiguration)
					analysisJson = result.Build().ToJsonString();
			}

			if (analysisJson == null)
				return baseJson ?? "{}";

			if (baseJson == null || IsEmptyJsonObject(baseJson))
				return $$"""{ "analysis": {{analysisJson}} }""";

			var lastBrace = baseJson.LastIndexOf('}');
			return baseJson.Substring(0, lastBrace).TrimEnd()
				+ $$""", "analysis": {{analysisJson}} }""";
		};
	}

	private static bool IsEmptyJsonObject(string json)
	{
		foreach (var c in json)
		{
			if (c != '{' && c != '}' && !char.IsWhiteSpace(c))
				return false;
		}
		return true;
	}
}
