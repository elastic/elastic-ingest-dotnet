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

namespace Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;

/// <summary>
/// Bootstrap step that creates component templates for settings and mappings.
/// </summary>
public class ComponentTemplateStep : IBootstrapStep
{
	/// <inheritdoc />
	public string Name => "ComponentTemplate";

	/// <inheritdoc />
	public async Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken ctx = default)
	{
		var (settingsName, settingsBody) = GetSettingsComponentTemplate(context);
		if (!await PutComponentTemplateAsync(context, settingsName, settingsBody, ctx).ConfigureAwait(false))
			return false;

		var (mappingsName, mappingsBody) = GetMappingsComponentTemplate(context);
		if (!await PutComponentTemplateAsync(context, mappingsName, mappingsBody, ctx).ConfigureAwait(false))
			return false;

		// Compute and store the channel hash
		context.ChannelHash = HashedBulkUpdate.CreateHash(settingsName, settingsBody, mappingsName, mappingsBody);
		return true;
	}

	/// <inheritdoc />
	public bool Execute(BootstrapContext context)
	{
		var (settingsName, settingsBody) = GetSettingsComponentTemplate(context);
		if (!PutComponentTemplate(context, settingsName, settingsBody))
			return false;

		var (mappingsName, mappingsBody) = GetMappingsComponentTemplate(context);
		if (!PutComponentTemplate(context, mappingsName, mappingsBody))
			return false;

		context.ChannelHash = HashedBulkUpdate.CreateHash(settingsName, settingsBody, mappingsName, mappingsBody);
		return true;
	}

	private static (string name, string body) GetSettingsComponentTemplate(BootstrapContext context)
	{
		var ilmPolicy = string.IsNullOrWhiteSpace(context.IlmPolicy) ? "logs" : context.IlmPolicy;

		var overallSettings = new Dictionary<string, string>();
		if (!context.IsServerless && ilmPolicy is not null)
			overallSettings["index.lifecycle.name"] = ilmPolicy;

		var settings = new StringBuilder("{");
		var settingsAsJson = string.Join(",\n", overallSettings.Select(kv => $"  \"{kv.Key}\": \"{kv.Value}\""));
		if (!string.IsNullOrWhiteSpace(settingsAsJson))
			settings.Append('\n').Append(settingsAsJson).Append('\n');
		settings.Append('}');

		var settingsName = $"{context.TemplateName}-settings";
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

	private static (string name, string body) GetMappingsComponentTemplate(BootstrapContext context)
	{
		var mappingsName = $"{context.TemplateName}-mappings";
		var mappings = context.GetMappingsJson?.Invoke() ?? "{}";
		var settings = context.GetMappingSettings?.Invoke() ?? "{}";
		var mappingsBody = $@"{{
              ""template"": {{
                ""settings"": {settings},
                ""mappings"": {mappings}
              }},
              ""_meta"": {{
                ""description"": ""Template installed by .NET ingest libraries (https://github.com/elastic/elastic-ingest-dotnet)"",
                ""assembly_version"": ""{LibraryVersion.Current}""
              }}
            }}";
		return (mappingsName, mappingsBody);
	}

	private static async Task<bool> PutComponentTemplateAsync(BootstrapContext context, string name, string body, CancellationToken ctx)
	{
		var response = await context.Transport.RequestAsync<StringResponse>(
			HttpMethod.PUT, $"_component_template/{name}", PostData.String(body), cancellationToken: ctx
		).ConfigureAwait(false);

		if (response.ApiCallDetails.HasSuccessfulStatusCode) return true;

		return context.BootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception(
				$"Failure to create component template `{name}` for {context.TemplateWildcard}: {response}",
				response.ApiCallDetails.OriginalException
			);
	}

	private static bool PutComponentTemplate(BootstrapContext context, string name, string body)
	{
		var response = context.Transport.Request<StringResponse>(
			HttpMethod.PUT, $"_component_template/{name}", PostData.String(body)
		);

		if (response.ApiCallDetails.HasSuccessfulStatusCode) return true;

		return context.BootstrapMethod == BootstrapMethod.Silent
			? false
			: throw new Exception(
				$"Failure to create component template `{name}` for {context.TemplateWildcard}: {response}",
				response.ApiCallDetails.OriginalException
			);
	}
}
