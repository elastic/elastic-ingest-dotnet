// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Transport;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;

/// <summary>
/// Bootstrap step that creates an index template (without data_stream).
/// Includes hash-based (and optional version-based) short-circuit: if the template
/// already exists with matching hash, or was deployed by a newer mapping version, skip.
/// </summary>
public class IndexTemplateStep : IBootstrapStep
{
	/// <inheritdoc />
	public string Name => "IndexTemplate";

	/// <inheritdoc />
	public async Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken ctx = default)
	{
		var templateExists = await IndexTemplateExistsAsync(context.Transport, context.TemplateName, ctx).ConfigureAwait(false);
		if (templateExists)
		{
			var meta = await TemplateMetadataHelper.FetchMetaAsync(context.Transport, context.TemplateName, ctx).ConfigureAwait(false);
			if (TemplateMetadataHelper.ShouldSkipBootstrap(meta, context.ChannelHash, context.MappingVersion))
				return true;
		}

		var body = BuildIndexTemplateBody(context);
		return await PutIndexTemplateAsync(context, context.TemplateName, body, ctx).ConfigureAwait(false);
	}

	/// <inheritdoc />
	public bool Execute(BootstrapContext context)
	{
		var templateExists = IndexTemplateExists(context.Transport, context.TemplateName);
		if (templateExists)
		{
			var meta = TemplateMetadataHelper.FetchMeta(context.Transport, context.TemplateName);
			if (TemplateMetadataHelper.ShouldSkipBootstrap(meta, context.ChannelHash, context.MappingVersion))
				return true;
		}

		var body = BuildIndexTemplateBody(context);
		return PutIndexTemplate(context, context.TemplateName, body);
	}

	/// <summary>
	/// Builds the index template JSON body. Can be overridden for customization.
	/// </summary>
	protected virtual string BuildIndexTemplateBody(BootstrapContext context)
	{
		var settingsName = $"{context.TemplateName}-settings";
		var mappingsName = $"{context.TemplateName}-mappings";
		var mappingVersionFragment = TemplateMetadataHelper.BuildMappingVersionFragment(context.MappingVersion);

		return @$"{{
                ""index_patterns"": [""{context.TemplateWildcard}""],
                ""composed_of"": [ ""{mappingsName}"", ""{settingsName}"" ],
                ""priority"": 201,
                ""_meta"": {{
                    ""description"": ""Template installed by .NET ingest libraries (https://github.com/elastic/elastic-ingest-dotnet)"",
                    ""assembly_version"": ""{LibraryVersion.Current}"",
                    ""hash"": ""{context.ChannelHash}""{mappingVersionFragment}
                }}
            }}";
	}

	private static async Task<bool> IndexTemplateExistsAsync(ITransport transport, string name, CancellationToken ctx)
	{
		var response = await transport.RequestAsync<StringResponse>(HttpMethod.HEAD, $"_index_template/{name}", cancellationToken: ctx).ConfigureAwait(false);
		return response.ApiCallDetails.HttpStatusCode is 200;
	}

	private static bool IndexTemplateExists(ITransport transport, string name)
	{
		var response = transport.Request<StringResponse>(HttpMethod.HEAD, $"_index_template/{name}");
		return response.ApiCallDetails.HttpStatusCode is 200;
	}

	private static async Task<bool> PutIndexTemplateAsync(BootstrapContext context, string name, string body, CancellationToken ctx)
	{
		var response = await context.Transport.RequestAsync<StringResponse>(
			HttpMethod.PUT, $"_index_template/{name}", PostData.String(body), cancellationToken: ctx
		).ConfigureAwait(false);

		if (response.ApiCallDetails.HasSuccessfulStatusCode) return true;

		return context.BootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception(
				$"Failure to create index template for {context.TemplateWildcard}: {response}",
				response.ApiCallDetails.OriginalException
			);
	}

	private static bool PutIndexTemplate(BootstrapContext context, string name, string body)
	{
		var response = context.Transport.Request<StringResponse>(
			HttpMethod.PUT, $"_index_template/{name}", PostData.String(body)
		);

		if (response.ApiCallDetails.HasSuccessfulStatusCode) return true;

		return context.BootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception(
				$"Failure to create index template for {context.TemplateWildcard}: {response}",
				response.ApiCallDetails.OriginalException
			);
	}
}
