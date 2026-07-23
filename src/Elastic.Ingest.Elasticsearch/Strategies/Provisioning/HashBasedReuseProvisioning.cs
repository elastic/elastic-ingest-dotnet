// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Provisioning strategy that reuses an existing index when the template hash matches.
/// Extracted from CatalogIndexChannel's TryReuseIndex logic.
/// </summary>
public class HashBasedReuseProvisioning : IIndexProvisioningStrategy
{
	/// <inheritdoc />
	public async Task<ProvisioningDecision> DecideAsync(BootstrapContext context, CancellationToken ctx = default)
	{
		var templateExists = await TemplateExistsAsync(context.Transport, context.TemplateName, ctx).ConfigureAwait(false);
		if (!templateExists)
			return ProvisioningDecision.CreateNew;

		var meta = await TemplateMetadataHelper.FetchMetaAsync(context.Transport, context.TemplateName, ctx).ConfigureAwait(false);
		return TemplateMetadataHelper.ShouldSkipBootstrap(meta, context.ChannelHash, context.MappingVersion)
			? ProvisioningDecision.ReuseExisting
			: ProvisioningDecision.CreateNew;
	}

	/// <inheritdoc />
	public ProvisioningDecision Decide(BootstrapContext context)
	{
		var templateExists = TemplateExists(context.Transport, context.TemplateName);
		if (!templateExists)
			return ProvisioningDecision.CreateNew;

		var meta = TemplateMetadataHelper.FetchMeta(context.Transport, context.TemplateName);
		return TemplateMetadataHelper.ShouldSkipBootstrap(meta, context.ChannelHash, context.MappingVersion)
			? ProvisioningDecision.ReuseExisting
			: ProvisioningDecision.CreateNew;
	}

	/// <inheritdoc />
	public async Task<string?> ResolveExistingIndexAsync(string indexPattern, string latestAlias, ITransport transport, CancellationToken ctx = default)
	{
		var hasPreviousVersions = await transport.HeadAsync($"{indexPattern}?allow_no_indices=false", ctx).ConfigureAwait(false);
		if (hasPreviousVersions.ApiCallDetails.HttpStatusCode != 200)
			return null;

		var rq = new RequestConfiguration { Accept = "text/plain" };
		var catResponse = await transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"_cat/aliases/{latestAlias}?h=index", null, rq, ctx
		).ConfigureAwait(false);

		var index = catResponse.Body.Trim(Environment.NewLine.ToCharArray());
		return string.IsNullOrEmpty(index) ? null : index;
	}

	/// <inheritdoc />
	public string? ResolveExistingIndex(string indexPattern, string latestAlias, ITransport transport)
	{
		var hasPreviousVersions = transport.Head($"{indexPattern}?allow_no_indices=false");
		if (hasPreviousVersions.ApiCallDetails.HttpStatusCode != 200)
			return null;

		var rq = new RequestConfiguration { Accept = "text/plain" };
		var catResponse = transport.Request<StringResponse>(
			HttpMethod.GET, $"_cat/aliases/{latestAlias}?h=index", null, rq
		);

		var index = catResponse.Body.Trim(Environment.NewLine.ToCharArray());
		return string.IsNullOrEmpty(index) ? null : index;
	}

	private static async Task<bool> TemplateExistsAsync(ITransport transport, string name, CancellationToken ctx)
	{
		var response = await transport.RequestAsync<StringResponse>(HttpMethod.HEAD, $"_index_template/{name}", cancellationToken: ctx).ConfigureAwait(false);
		return response.ApiCallDetails.HttpStatusCode is 200;
	}

	private static bool TemplateExists(ITransport transport, string name)
	{
		var response = transport.Request<StringResponse>(HttpMethod.HEAD, $"_index_template/{name}");
		return response.ApiCallDetails.HttpStatusCode is 200;
	}

}
