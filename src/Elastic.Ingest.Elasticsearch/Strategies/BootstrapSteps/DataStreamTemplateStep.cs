// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Linq;
using Elastic.Ingest.Transport;

namespace Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;

/// <summary>
/// Bootstrap step that creates an index template with <c>"data_stream": {}</c>.
/// Includes inferred component templates for logs/metrics data streams.
/// </summary>
public class DataStreamTemplateStep : IndexTemplateStep
{
	/// <inheritdoc />
	public new string Name => "DataStreamTemplate";

	/// <inheritdoc />
	protected override string BuildIndexTemplateBody(BootstrapContext context)
	{
		var settingsName = $"{context.TemplateName}-settings";
		var mappingsName = $"{context.TemplateName}-mappings";

		var additionalComponents = GetInferredComponentTemplates(context);
		var additionalComponentsJson = string.Join(", ", additionalComponents.Select(a => $"\"{a}\""));

		return @$"{{
                ""index_patterns"": [""{context.TemplateWildcard}""],
                ""data_stream"": {{ }},
                ""composed_of"": [ ""{mappingsName}"", ""{settingsName}"", {additionalComponentsJson} ],
                ""priority"": 201,
                ""_meta"": {{
                    ""description"": ""Template installed by .NET ingest libraries (https://github.com/elastic/elastic-ingest-dotnet)"",
                    ""assembly_version"": ""{LibraryVersion.Current}"",
                    ""hash"": ""{context.ChannelHash}""
                }}
            }}";
	}

	/// <summary>
	/// Gets additional built-in component templates based on the data stream type.
	/// </summary>
	protected static List<string> GetInferredComponentTemplates(BootstrapContext context)
	{
		var components = new List<string> { "data-streams-mappings" };

		if (context.DataStreamType != null)
		{
			var type = context.DataStreamType.ToLowerInvariant();
			if (type == "logs")
				components.AddRange(["logs-settings", "logs-mappings"]);
			else if (type == "metrics")
				components.AddRange(["metrics-settings", "metrics-mappings"]);
		}

		return components;
	}
}
