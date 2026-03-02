// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping;

/// <summary>
/// Bundles resolved AI enrichment infrastructure names and bodies.
/// Use this when the lookup index name must be derived at runtime
/// (e.g., from a <see cref="ElasticsearchTypeContext"/> resolved via <c>CreateContext</c>).
/// <para>
/// Created by the source-generated <c>CreateInfrastructure(string)</c> method on the
/// <see cref="IAiEnrichmentProvider"/> implementation, or by <see cref="FromProvider"/>
/// to use the provider's compile-time defaults.
/// </para>
/// </summary>
public sealed class AiInfrastructure
{
	/// <summary>The name of the lookup index that stores enrichment data.</summary>
	public string LookupIndexName { get; }

	/// <summary>The mapping JSON for the lookup index.</summary>
	public string LookupIndexMapping { get; }

	/// <summary>The document field used as the match key between lookup and documents.</summary>
	public string MatchField { get; }

	/// <summary>A short hash derived from the enrichment field names.</summary>
	public string FieldsHash { get; }

	/// <summary>The name of the Elasticsearch enrich policy.</summary>
	public string EnrichPolicyName { get; }

	/// <summary>The JSON body for creating the enrich policy.</summary>
	public string EnrichPolicyBody { get; }

	/// <summary>The name of the ingest pipeline.</summary>
	public string PipelineName { get; }

	/// <summary>The JSON body for creating the ingest pipeline.</summary>
	public string PipelineBody { get; }

	/// <inheritdoc cref="AiInfrastructure"/>
	public AiInfrastructure(
		string lookupIndexName,
		string lookupIndexMapping,
		string matchField,
		string fieldsHash,
		string enrichPolicyName,
		string enrichPolicyBody,
		string pipelineName,
		string pipelineBody)
	{
		LookupIndexName = lookupIndexName;
		LookupIndexMapping = lookupIndexMapping;
		MatchField = matchField;
		FieldsHash = fieldsHash;
		EnrichPolicyName = enrichPolicyName;
		EnrichPolicyBody = enrichPolicyBody;
		PipelineName = pipelineName;
		PipelineBody = pipelineBody;
	}

	/// <summary>
	/// Creates infrastructure using the provider's compile-time default names.
	/// </summary>
	public static AiInfrastructure FromProvider(IAiEnrichmentProvider provider) =>
		new(
			provider.LookupIndexName,
			provider.LookupIndexMapping,
			provider.MatchField,
			provider.FieldsHash,
			provider.EnrichPolicyName,
			provider.EnrichPolicyBody,
			provider.PipelineName,
			provider.PipelineBody);
}
