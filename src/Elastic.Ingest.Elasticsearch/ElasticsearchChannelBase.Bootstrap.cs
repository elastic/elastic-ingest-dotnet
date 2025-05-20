// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Transport;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch;

public abstract partial class ElasticsearchChannelBase<TEvent, TChannelOptions>
	where TChannelOptions : ElasticsearchChannelOptionsBase<TEvent>
	where TEvent : class
{
	/// <summary> The index template name <see cref="BootstrapElasticsearch"/> should register.</summary>
	protected abstract string TemplateName { get; }
	/// <summary> The index template wildcard the <see cref="BootstrapElasticsearch"/> should register for its index template.</summary>
	protected abstract string TemplateWildcard { get; }

	/// <summary>
	/// Returns a minimal default index template for an <see cref="ElasticsearchChannelBase{TEvent, TChannelOptions}"/> implementation
	/// </summary>
	/// <returns>A tuple of (name, body) describing the index template</returns>
	protected abstract (string, string) GetDefaultIndexTemplate(string name, string match, string mappingsName, string settingsName);

	/// <summary>
	/// Bootstrap the target data stream. Will register the appropriate index and component templates
	/// </summary>
	/// <param name="bootstrapMethod">Either None (no bootstrapping), Silent (quiet exit), Failure (throw exceptions)</param>
	/// <param name="ilmPolicy">Registers a component template that ensures the template is managed by this ilm policy</param>
	/// <param name="ctx"></param>
	public virtual async Task<bool> BootstrapElasticsearchAsync(BootstrapMethod bootstrapMethod, string? ilmPolicy = null, CancellationToken ctx = default)
	{
		if (bootstrapMethod == BootstrapMethod.None) return true;

		ctx = ctx == CancellationToken.None ? TokenSource.Token : ctx;

		var name = TemplateName;
		var match = TemplateWildcard;
		if (await IndexTemplateExistsAsync(name, ctx).ConfigureAwait(false)) return false;

		var (settingsName, settingsBody) = GetDefaultComponentSettings(bootstrapMethod, name, ilmPolicy);
		if (!await PutComponentTemplateAsync(bootstrapMethod, settingsName, settingsBody, ctx).ConfigureAwait(false))
			return false;

		var (mappingsName, mappingsBody) = GetDefaultComponentMappings(name);
		if (!await PutComponentTemplateAsync(bootstrapMethod, mappingsName, mappingsBody, ctx).ConfigureAwait(false))
			return false;

		var (indexTemplateName, indexTemplateBody) = GetDefaultIndexTemplate(name, match, mappingsName, settingsName);
		if (!await PutIndexTemplateAsync(bootstrapMethod, indexTemplateName, indexTemplateBody, ctx).ConfigureAwait(false))
			return false;

		return true;
	}

	/// <summary>
	/// Bootstrap the target data stream. Will register the appropriate index and component templates
	/// </summary>
	/// <param name="bootstrapMethod">Either None (no bootstrapping), Silent (quiet exit), Failure (throw exceptions)</param>
	/// <param name="ilmPolicy">Registers a component template that ensures the template is managed by this ilm policy</param>
	public virtual bool BootstrapElasticsearch(BootstrapMethod bootstrapMethod, string? ilmPolicy = null)
	{
		if (bootstrapMethod == BootstrapMethod.None) return true;

		var name = TemplateName;
		var match = TemplateWildcard;
		if (IndexTemplateExists(name)) return false;

		var (settingsName, settingsBody) = GetDefaultComponentSettings(bootstrapMethod, name, ilmPolicy);
		if (!PutComponentTemplate(bootstrapMethod, settingsName, settingsBody))
			return false;

		var (mappingsName, mappingsBody) = GetDefaultComponentMappings(name);
		if (!PutComponentTemplate(bootstrapMethod, mappingsName, mappingsBody))
			return false;

		var (indexTemplateName, indexTemplateBody) = GetDefaultIndexTemplate(name, match, mappingsName, settingsName);
		if (!PutIndexTemplate(bootstrapMethod, indexTemplateName, indexTemplateBody))
			return false;

		return true;
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
	/// Returns default component settings template for a <see cref="ElasticsearchChannelBase{TEvent, TChannelOptions}"/>
	/// </summary>
	/// <returns>A tuple of (name, body) describing the default component template settings</returns>
	protected (string, string) GetDefaultComponentSettings(BootstrapMethod bootstrapMethod, string indexTemplateName, string? ilmPolicy = null)
	{
		if (string.IsNullOrWhiteSpace(ilmPolicy))
			ilmPolicy = "logs";

		var settings = "{}";
		if (!IsServerless(bootstrapMethod))
			settings = $@"{{
                  ""index.lifecycle.name"": ""{ilmPolicy}""
                }}";

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

	/// <summary>
	/// Returns a minimal default mapping component settings template for a <see cref="ElasticsearchChannelBase{TEvent, TChannelOptions}"/>
	/// </summary>
	/// <returns>A tuple of (name, body) describing the default component template mappings</returns>
	protected (string, string) GetDefaultComponentMappings(string indexTemplateName)
	{
		var settingsName = $"{indexTemplateName}-mappings";
		var settingsBody = $@"{{
              ""template"": {{
                ""mappings"": {{
                }}
              }},
              ""_meta"": {{
                ""description"": ""Template installed by .NET ingest libraries (https://github.com/elastic/elastic-ingest-dotnet)"",
                ""assembly_version"": ""{LibraryVersion.Current}""
              }}
            }}";
		return (settingsName, settingsBody);
	}

}
