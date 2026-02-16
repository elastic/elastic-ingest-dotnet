// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Transport;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch;

public abstract partial class IngestChannelBase<TDocument, TChannelOptions>
	where TChannelOptions : IngestChannelOptionsBase<TDocument>
	where TDocument : class
{
	/// <summary> The index template name <see cref="BootstrapElasticsearch"/> should register.</summary>
	protected abstract string TemplateName { get; }
	/// <summary> The index template wildcard the <see cref="BootstrapElasticsearch"/> should register for its index template.</summary>
	protected abstract string TemplateWildcard { get; }

	/// Allow implementations to override the default behavior of always bootstrapping the component templates
	/// The default is false, meaning we won't register the component templates again if the index template already exists.
	protected virtual bool AlwaysBootstrapComponentTemplates => false;

	/// <summary> A unique hash calculated when <see cref="BootstrapElasticsearchAsync"/> is called</summary>
	public string ChannelHash { get; protected set; } = string.Empty;

	/// <summary>
	/// Returns a minimal default index template for an <see cref="IngestChannelBase{TEvent, TChannelOptions}"/> implementation
	/// </summary>
	/// <returns>A tuple of (name, body) describing the index template</returns>
	protected abstract (string, string) GetDefaultIndexTemplate(string name, string match, string mappingsName, string settingsName, string hash);

	/// <summary>
	/// Bootstrap the target data stream. Will register the appropriate index and component templates
	/// </summary>
	/// <param name="bootstrapMethod">Either None (no bootstrapping), Silent (quiet exit), Failure (throw exceptions)</param>
	/// <param name="ctx"></param>
	public virtual async Task<bool> BootstrapElasticsearchAsync(BootstrapMethod bootstrapMethod, CancellationToken ctx = default)
	{
		ctx = ctx == CancellationToken.None ? TokenSource.Token : ctx;

		GenerateChannelHash(bootstrapMethod, out var settingsName, out var settingsBody, out var mappingsName, out var mappingsBody);

		if (bootstrapMethod == BootstrapMethod.None) return true;

		var indexTemplateExists = await IndexTemplateExistsAsync(TemplateName, ctx).ConfigureAwait(false);
		var indexTemplateMatchesHash = indexTemplateExists && await IndexTemplateMatchesHashAsync(ChannelHash, ctx).ConfigureAwait(false);
		if (indexTemplateExists && !AlwaysBootstrapComponentTemplates && indexTemplateMatchesHash)
			return true;

		if (!await PutComponentTemplateAsync(bootstrapMethod, settingsName, settingsBody, ctx).ConfigureAwait(false))
			return false;

		if (!await PutComponentTemplateAsync(bootstrapMethod, mappingsName, mappingsBody, ctx).ConfigureAwait(false))
			return false;

		if (indexTemplateExists && indexTemplateMatchesHash)
			return true;

		var (indexTemplateName, indexTemplateBody) = GetDefaultIndexTemplate(TemplateName, TemplateWildcard, mappingsName, settingsName, ChannelHash);
		if (!await PutIndexTemplateAsync(bootstrapMethod, indexTemplateName, indexTemplateBody, ctx).ConfigureAwait(false))
			return false;

		return true;
	}

	/// <summary> Generate the channel hash </summary>
	protected void GenerateChannelHash(
		BootstrapMethod bootstrapMethod,
		out string settingsName,
		out string settingsBody,
		out string mappingsName,
		out string mappingsBody
	)
	{
		(settingsName, settingsBody) = GetDefaultComponentSettings(bootstrapMethod, TemplateName);
		(mappingsName, mappingsBody) = GetDefaultComponentMappings(TemplateName);

		var hash = HashedBulkUpdate.CreateHash(settingsName, settingsBody, mappingsName, mappingsBody);
		ChannelHash = hash;
	}

	/// <summary>
	/// Bootstrap the target data stream. Will register the appropriate index and component templates
	/// </summary>
	/// <param name="bootstrapMethod">Either None (no bootstrapping), Silent (quiet exit), Failure (throw exceptions)</param>
	public virtual bool BootstrapElasticsearch(BootstrapMethod bootstrapMethod)
	{
		GenerateChannelHash(bootstrapMethod, out var settingsName, out var settingsBody, out var mappingsName, out var mappingsBody);

		if (bootstrapMethod == BootstrapMethod.None) return true;

		//if the index template already exists and has the same hash, we don't need to re-register the component templates'
		var indexTemplateExists = IndexTemplateExists(TemplateName);
		var indexTemplateMatchesHash = indexTemplateExists && IndexTemplateMatchesHash(ChannelHash);
		if (indexTemplateExists && !AlwaysBootstrapComponentTemplates && indexTemplateMatchesHash)
			return true;

		if (!PutComponentTemplate(bootstrapMethod, settingsName, settingsBody))
			return false;

		if (!PutComponentTemplate(bootstrapMethod, mappingsName, mappingsBody))
			return false;

		if (indexTemplateExists && indexTemplateMatchesHash)
			return true;

		var (indexTemplateName, indexTemplateBody) = GetDefaultIndexTemplate(TemplateName, TemplateWildcard, mappingsName, settingsName, ChannelHash);
		if (!PutIndexTemplate(bootstrapMethod, indexTemplateName, indexTemplateBody))
			return false;

		return true;
	}

	/// <summary> Checks if the stored hash matches</summary>
	public bool IndexTemplateMatchesHash(string hash)
	{
		var metaHash = GetIndexTemplateHash();
		// if the hash is empty, we don't have a hash stored, so we don't match
		if (string.IsNullOrWhiteSpace(metaHash))
			return false;
	 	return metaHash == hash;
	}

	/// <summary> Gets the stored hash of the index template and its generated components </summary>
	public async Task<bool> IndexTemplateMatchesHashAsync(string hash, CancellationToken ctx = default)
	{
		var metaHash = await GetIndexTemplateHashAsync(ctx).ConfigureAwait(false);
		if (string.IsNullOrWhiteSpace(metaHash))
			return false;
		return metaHash == hash;
	}


	/// <summary> Get the stored hash on the index template if available </summary>
	public string? GetIndexTemplateHash()
	{
		var template = Options.Transport.Request<StringResponse>(HttpMethod.GET, $"/_index_template/{TemplateName}?filter_path=index_templates.index_template._meta.hash");
		if (!template.ApiCallDetails.HasSuccessfulStatusCode)
			return string.Empty;
		return ReadMetaHash(template);
	}

	private static string? ReadMetaHash(StringResponse template)
	{
		var metaHash = template.Body.Replace("""{"index_templates":[{"index_template":{"_meta":{"hash":""", "").Trim('\"').Split('\"').FirstOrDefault();
		return metaHash;
	}

	/// <summary> Get the stored hash on the index template if available </summary>
	public async Task<string?> GetIndexTemplateHashAsync(CancellationToken ctx)
	{
		var template = await Options.Transport.RequestAsync<StringResponse>(HttpMethod.GET, $"/_index_template/{TemplateName}?filter_path=index_templates.index_template._meta.hash", ctx)
			.ConfigureAwait(false);
		return ReadMetaHash(template);
	}

	private bool? _isServerless;
	/// Detects whether we are running against serverless or not
	protected bool IsServerless(BootstrapMethod bootstrapMethod)
	{
		if (_isServerless.HasValue)
			return _isServerless.Value;
		var rootInfo = Options.Transport.Request<DynamicResponse>(HttpMethod.GET, $"/");
		var statusCode = rootInfo.ApiCallDetails.HttpStatusCode;
		if (statusCode is not 200)
			return bootstrapMethod == BootstrapMethod.Silent
				? false
				: throw new Exception(
					$"Failure to check whether instance is serverless or not {rootInfo}",
					rootInfo.ApiCallDetails.OriginalException
				);
		var flavor = rootInfo.Body.Get<string>("version.build_flavor");
		_isServerless = statusCode is 200 && flavor == "serverless";
		return _isServerless.Value;

	}

	/// The indices and/o datastreams to refresh as part of this implementation
	protected abstract string RefreshTargets { get; }

	/// Refresh all targets that were written too
	public bool Refresh()
	{
		var url = $"{RefreshTargets}/_refresh?allow_no_indices=true&ignore_unavailable=true";
		var refresh = Options.Transport.Request<RefreshResponse>(HttpMethod.POST, url, PostData.Empty);
		var statusCode = refresh.ApiCallDetails.HttpStatusCode;
		return statusCode is 200;
	}

	/// Refresh all targets that were written too
	public async Task<bool> RefreshAsync(CancellationToken ctx = default)
	{
		var url = $"{RefreshTargets}/_refresh?allow_no_indices=true&ignore_unavailable=true";
		var refresh = await Options.Transport.RequestAsync<RefreshResponse>(HttpMethod.POST, url, PostData.Empty, ctx)
			.ConfigureAwait(false);
		var statusCode = refresh.ApiCallDetails.HttpStatusCode;
		return statusCode is 200;
	}


	/// <summary></summary>
	protected bool IndexTemplateExists(string name)
	{
		var templateExists = Options.Transport.Request<HeadIndexTemplateResponse>(HttpMethod.HEAD, $"_index_template/{name}");
		var statusCode = templateExists.ApiCallDetails.HttpStatusCode;
		return statusCode is 200;
	}

	/// <summary></summary>
	protected async Task<bool> IndexTemplateExistsAsync(string name, CancellationToken ctx = default)
	{
		var templateExists = await Options.Transport.RequestAsync<HeadIndexTemplateResponse>
				(HttpMethod.HEAD, $"_index_template/{name}", cancellationToken: ctx)
			.ConfigureAwait(false);
		var statusCode = templateExists.ApiCallDetails.HttpStatusCode;
		return statusCode is 200;
	}

	/// <summary></summary>
	protected bool PutIndexTemplate(BootstrapMethod bootstrapMethod, string name, string body)
	{
		var putIndexTemplateResponse = Options.Transport.Request<PutIndexTemplateResponse>(
			HttpMethod.PUT, $"_index_template/{name}", PostData.String(body)
		);
		if (putIndexTemplateResponse.ApiCallDetails.HasSuccessfulStatusCode) return true;


		return bootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception(
				$"Failure to create index templates for {TemplateWildcard}: {putIndexTemplateResponse}",
				putIndexTemplateResponse.ApiCallDetails.OriginalException
			);
	}

	/// <summary></summary>
	protected async Task<bool> PutIndexTemplateAsync(BootstrapMethod bootstrapMethod, string name, string body, CancellationToken ctx = default)
	{
		var putIndexTemplateResponse = await Options.Transport.RequestAsync<PutIndexTemplateResponse>
				(HttpMethod.PUT, $"_index_template/{name}", PostData.String(body), cancellationToken: ctx)
			.ConfigureAwait(false);
		if (putIndexTemplateResponse.ApiCallDetails.HasSuccessfulStatusCode) return true;

		return bootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception(
				$"Failure to create index templates for {TemplateWildcard}: {putIndexTemplateResponse}",
				putIndexTemplateResponse.ApiCallDetails.OriginalException
			);
	}

	/// <summary></summary>
	protected bool PutComponentTemplate(BootstrapMethod bootstrapMethod, string name, string body)
	{
		var putComponentTemplate = Options.Transport.Request<PutComponentTemplateResponse>
			(HttpMethod.PUT, $"_component_template/{name}", PostData.String(body));
		if (putComponentTemplate.ApiCallDetails.HasSuccessfulStatusCode) return true;

		return bootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception(
				$"Failure to create component template `{name}` for {TemplateWildcard}: {putComponentTemplate}",
				putComponentTemplate.ApiCallDetails.OriginalException
			);
	}

	/// <summary></summary>
	protected async Task<bool> PutComponentTemplateAsync(BootstrapMethod bootstrapMethod, string name, string body, CancellationToken ctx = default)
	{
		var putComponentTemplate = await Options.Transport.RequestAsync<PutComponentTemplateResponse>
				(HttpMethod.PUT, $"_component_template/{name}", PostData.String(body), cancellationToken: ctx)
			.ConfigureAwait(false);
		if (putComponentTemplate.ApiCallDetails.HasSuccessfulStatusCode) return true;

		return bootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception(
				$"Failure to create component template `{name}` for {TemplateWildcard}: {putComponentTemplate}",
				putComponentTemplate.ApiCallDetails.OriginalException
			);
	}

	/// <summary>
	/// Returns default component settings template for a <see cref="IngestChannelBase{TEvent, TChannelOptions}"/>
	/// </summary>
	/// <returns>A tuple of (name, body) describing the default component template settings</returns>
	protected (string, string) GetDefaultComponentSettings(BootstrapMethod bootstrapMethod, string indexTemplateName)
	{
		var injectedSettings = GetDefaultComponentIndexSettings();
		var overallSettings = new Dictionary<string, string>();
		foreach (var kv in injectedSettings)
			overallSettings[kv.Key] = kv.Value;

		var settings = new StringBuilder("{");
		var settingsAsJson = string.Join(",\n", overallSettings.Select(kv => $"  \"{kv.Key}\": \"{kv.Value}\""));
		if (!string.IsNullOrWhiteSpace(settingsAsJson))
			settings.Append('\n').Append(settingsAsJson).Append('\n');
		settings.Append('}');

		var settingsName = $"{indexTemplateName}-settings";
		var settingsBody = $@"{{
              ""template"": {{
                ""settings"": {settings}
              }},
              ""_meta"": {{
                ""description"": ""Template installed by .NET ingest libraries (https://github.com/elastic/elastic-ingest-dotnet)"",
                ""assembly_version"": ""{LibraryVersion.Current}""
              }}
            }}";
		return (settingsName, settingsBody);
	}

	/// Allows implementations of <see cref="IngestChannelBase{TEvent, TChannelOptions}"/> to inject additional component index settings
	protected virtual IReadOnlyDictionary<string, string> GetDefaultComponentIndexSettings() => new Dictionary<string, string>();

	/// <summary>
	/// Returns a minimal default mapping component settings template for a <see cref="IngestChannelBase{TEvent, TChannelOptions}"/>
	/// </summary>
	/// <returns>A tuple of (name, body) describing the default component template mappings</returns>
	private (string, string) GetDefaultComponentMappings(string indexTemplateName)
	{
		var settingsName = $"{indexTemplateName}-mappings";
		var mappings = GetMappings() ?? "{}";
		var settings = GetMappingSettings() ?? "{}";
		var settingsBody = $@"{{
              ""template"": {{
                ""settings"": {settings},
                ""mappings"": {mappings}
              }},
              ""_meta"": {{
                ""description"": ""Template installed by .NET ingest libraries (https://github.com/elastic/elastic-ingest-dotnet)"",
                ""assembly_version"": ""{LibraryVersion.Current}""
              }}
            }}";
		return (settingsName, settingsBody);
	}

	/// Allows implementations of <see cref="IngestChannelBase{TEvent, TChannelOptions}"/> to inject mappings for <typeparamref name="TDocument"/>
	protected virtual string? GetMappings() => null;

	/// Allows implementations of <see cref="IngestChannelBase{TEvent, TChannelOptions}"/> to inject settings allong with <see cref="GetMappings"/> for <typeparamref name="TDocument"/>
	protected virtual string? GetMappingSettings() => null;

}
