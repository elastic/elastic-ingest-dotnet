// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Specialized;
using Elastic.Transport;
using Microsoft.Extensions.Configuration;

namespace Elastic.AgentBuilder.IntegrationTests;

/// <summary>
/// Base class for Agent Builder integration tests.
/// Reads Kibana connection details from user secrets.
/// </summary>
public abstract class AgentBuilderTestBase : IDisposable
{
	private static readonly NameValueCollection KibanaHeaders = new() { { "kbn-xsrf", "true" } };

	protected AgentBuilderClient Client { get; }
	protected string? CloudId { get; }

	protected AgentBuilderTestBase()
	{
		var config = new ConfigurationBuilder()
			.AddUserSecrets(typeof(AgentBuilderTestBase).Assembly, optional: true)
			.Build();

		var kibanaUrl = config["Parameters:KibanaUrl"];
		var apiKey = config["Parameters:KibanaApiKey"];
		CloudId = config["Parameters:CloudId"];
		var space = config["Parameters:KibanaSpace"];

		ITransport transport;
		if (!string.IsNullOrEmpty(CloudId) && !string.IsNullOrEmpty(apiKey))
		{
			var descriptor = new TransportConfigurationDescriptor(CloudId, new ApiKey(apiKey), CloudService.Kibana)
				.GlobalHeaders(KibanaHeaders);
			transport = new DistributedTransport(descriptor);
		}
		else if (!string.IsNullOrEmpty(kibanaUrl) && !string.IsNullOrEmpty(apiKey))
		{
			var descriptor = new TransportConfigurationDescriptor(new SingleNodePool(new Uri(kibanaUrl)))
				.Authentication(new ApiKey(apiKey))
				.GlobalHeaders(KibanaHeaders);
			transport = new DistributedTransport(descriptor);
		}
		else
		{
			throw new InvalidOperationException(
				"Integration tests require user secrets. Run from tests/Elastic.AgentBuilder.IntegrationTests:\n" +
				"\n" +
				"  dotnet user-secrets set \"Parameters:CloudId\" \"<cloud-id>\"\n" +
				"  dotnet user-secrets set \"Parameters:KibanaApiKey\" \"<base64-api-key>\"\n" +
				"\n" +
				"Or use a direct Kibana URL instead of CloudId:\n" +
				"\n" +
				"  dotnet user-secrets set \"Parameters:KibanaUrl\" \"https://my-kibana:5601\"\n" +
				"  dotnet user-secrets set \"Parameters:KibanaApiKey\" \"<base64-api-key>\"\n" +
				"\n" +
				"Optional: dotnet user-secrets set \"Parameters:KibanaSpace\" \"<space-name>\"");
		}

		Client = new AgentBuilderClient(transport, space);
	}

	public virtual void Dispose() => GC.SuppressFinalize(this);
}
