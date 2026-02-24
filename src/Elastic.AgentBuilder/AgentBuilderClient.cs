// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.AgentBuilder;

/// <summary>
/// Client for the Elastic Agent Builder Kibana API.
/// Provides CRUD operations for tools, agents, and conversations.
/// </summary>
public partial class AgentBuilderClient
{
	private readonly ITransport _transport;
	private readonly string _pathPrefix;

	/// <summary>
	/// Creates a new <see cref="AgentBuilderClient"/> from an <see cref="AgentTransportConfiguration"/>.
	/// This is the recommended constructor — it auto-wires the <c>kbn-xsrf</c> header and
	/// reads the <see cref="AgentTransportConfiguration.Space"/> for path prefixing.
	/// </summary>
	public AgentBuilderClient(AgentTransportConfiguration configuration)
		: this(new AgentTransport(configuration), configuration.Space)
	{
	}

	/// <summary>
	/// Creates a new <see cref="AgentBuilderClient"/> from a pre-configured <see cref="AgentTransport"/>.
	/// </summary>
	public AgentBuilderClient(AgentTransport transport)
		: this(transport, transport.AgentConfiguration.Space)
	{
	}

	/// <summary>
	/// Creates a new <see cref="AgentBuilderClient"/> from a raw <see cref="ITransport"/>.
	/// You are responsible for setting the <c>kbn-xsrf</c> header on the transport.
	/// </summary>
	/// <param name="transport">An <see cref="ITransport"/> configured for Kibana.</param>
	/// <param name="space">Optional Kibana space name. When set, all API paths are prefixed with <c>/s/{space}</c>.</param>
	public AgentBuilderClient(ITransport transport, string? space = null)
	{
		_transport = transport ?? throw new ArgumentNullException(nameof(transport));
		_pathPrefix = string.IsNullOrWhiteSpace(space)
			? "/api/agent_builder"
			: $"/s/{space}/api/agent_builder";
	}

	private string Path(string relative) => $"{_pathPrefix}{relative}";

	private async Task<TResponse> GetAsync<TResponse>(string path, JsonTypeInfo<TResponse> typeInfo, CancellationToken ct)
	{
		var response = await _transport
			.RequestAsync<StringResponse>(HttpMethod.GET, Path(path), cancellationToken: ct)
			.ConfigureAwait(false);
		EnsureSuccess(response);
		return Deserialize(response.Body, typeInfo);
	}

	private async Task<TResponse> PostAsync<TRequest, TResponse>(
		string path, TRequest body, JsonTypeInfo<TRequest> requestTypeInfo, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken ct)
	{
		var json = JsonSerializer.Serialize(body, requestTypeInfo);
		var response = await _transport
			.RequestAsync<StringResponse>(HttpMethod.POST, Path(path), PostData.String(json), cancellationToken: ct)
			.ConfigureAwait(false);
		EnsureSuccess(response);
		return Deserialize(response.Body, responseTypeInfo);
	}

	private async Task<TResponse> PutAsync<TRequest, TResponse>(
		string path, TRequest body, JsonTypeInfo<TRequest> requestTypeInfo, JsonTypeInfo<TResponse> responseTypeInfo, CancellationToken ct)
	{
		var json = JsonSerializer.Serialize(body, requestTypeInfo);
		var response = await _transport
			.RequestAsync<StringResponse>(HttpMethod.PUT, Path(path), PostData.String(json), cancellationToken: ct)
			.ConfigureAwait(false);
		EnsureSuccess(response);
		return Deserialize(response.Body, responseTypeInfo);
	}

	private async Task DeleteAsync(string path, CancellationToken ct)
	{
		var response = await _transport
			.RequestAsync<StringResponse>(HttpMethod.DELETE, Path(path), cancellationToken: ct)
			.ConfigureAwait(false);
		EnsureSuccess(response);
	}

	private static TResponse Deserialize<TResponse>(string body, JsonTypeInfo<TResponse> typeInfo) =>
		JsonSerializer.Deserialize(body, typeInfo)
		?? throw new InvalidOperationException($"Failed to deserialize response as {typeof(TResponse).Name}");

	private static void EnsureSuccess(StringResponse response)
	{
		if (!response.ApiCallDetails.HasSuccessfulStatusCode)
		{
			throw new AgentBuilderException(
				$"Agent Builder API returned {response.ApiCallDetails.HttpStatusCode}: {response.Body}",
				response.ApiCallDetails);
		}
	}
}

/// <summary>
/// Exception thrown when the Agent Builder API returns a non-success status code.
/// </summary>
public class AgentBuilderException : Exception
{
	public ApiCallDetails ApiCallDetails { get; }

	public AgentBuilderException(string message, ApiCallDetails apiCallDetails)
		: base(message) =>
		ApiCallDetails = apiCallDetails;
}
