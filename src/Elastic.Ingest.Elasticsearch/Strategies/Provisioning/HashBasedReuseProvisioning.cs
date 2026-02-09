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
		// Check if template exists and matches hash - if so, we might reuse
		var templateExists = await TemplateExistsAsync(context.Transport, context.TemplateName, ctx).ConfigureAwait(false);
		if (!templateExists)
			return ProvisioningDecision.CreateNew;

		var matches = await TemplateMatchesHashAsync(context.Transport, context.TemplateName, context.ChannelHash, ctx).ConfigureAwait(false);
		return matches ? ProvisioningDecision.ReuseExisting : ProvisioningDecision.CreateNew;
	}

	/// <inheritdoc />
	public ProvisioningDecision Decide(BootstrapContext context)
	{
		var templateExists = TemplateExists(context.Transport, context.TemplateName);
		if (!templateExists)
			return ProvisioningDecision.CreateNew;

		var matches = TemplateMatchesHash(context.Transport, context.TemplateName, context.ChannelHash);
		return matches ? ProvisioningDecision.ReuseExisting : ProvisioningDecision.CreateNew;
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

	private static async Task<bool> TemplateMatchesHashAsync(ITransport transport, string name, string hash, CancellationToken ctx)
	{
		var response = await transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_index_template/{name}?filter_path=index_templates.index_template._meta.hash", ctx
		).ConfigureAwait(false);
		if (!response.ApiCallDetails.HasSuccessfulStatusCode)
			return false;
		var metaHash = response.Body
			.Replace("""{"index_templates":[{"index_template":{"_meta":{"hash":""", "")
			.Trim('"').Split('"')[0];
		return metaHash == hash;
	}

	private static bool TemplateMatchesHash(ITransport transport, string name, string hash)
	{
		var response = transport.Request<StringResponse>(
			HttpMethod.GET, $"/_index_template/{name}?filter_path=index_templates.index_template._meta.hash"
		);
		if (!response.ApiCallDetails.HasSuccessfulStatusCode)
			return false;
		var metaHash = response.Body
			.Replace("""{"index_templates":[{"index_template":{"_meta":{"hash":""", "")
			.Trim('"').Split('"')[0];
		return metaHash == hash;
	}
}
