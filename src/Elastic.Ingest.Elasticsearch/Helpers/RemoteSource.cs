// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System.Collections.Generic;

namespace Elastic.Ingest.Elasticsearch.Helpers;

/// <summary>
/// Configures a remote Elasticsearch cluster as the source for a <see cref="ServerReindex"/> operation.
/// Maps to the <c>source.remote</c> block in the <c>_reindex</c> request body.
/// <para>
/// In Elastic Cloud Serverless, only Elastic Cloud endpoints (ECH deployments and other Serverless
/// projects) are accepted — no allowlist configuration is needed.
/// In self-managed or ECH deployments the remote host must be listed in
/// <c>reindex.remote.whitelist</c> in <c>elasticsearch.yml</c>.
/// </para>
/// </summary>
public class RemoteSource
{
	/// <summary>
	/// The remote Elasticsearch endpoint including scheme, host, and port.
	/// For example: <c>https://my-deployment.es.us-east-1.aws.elastic.cloud:443</c>.
	/// </summary>
	public string Host { get; init; } = null!;

	/// <summary> Username for basic authentication on the remote cluster. </summary>
	public string? Username { get; init; }

	/// <summary> Password for basic authentication on the remote cluster. </summary>
	public string? Password { get; init; }

	/// <summary>
	/// API key for the remote cluster. Emitted as the native <c>api_key</c> field in the
	/// <c>source.remote</c> block. Supported by Elasticsearch 8.x+ / Elastic Stack.
	/// <para>
	/// For Serverless-to-Serverless reindex where the remote expects a raw <c>Authorization</c>
	/// header, use <see cref="Headers"/> instead:
	/// <c>Headers = new() { ["Authorization"] = "ApiKey base64value" }</c>.
	/// </para>
	/// </summary>
	public string? ApiKey { get; init; }

	/// <summary>
	/// Custom HTTP headers sent with every request to the remote cluster.
	/// Use this for Serverless auth where the remote expects a raw <c>Authorization</c> header,
	/// e.g. <c>new Dictionary&lt;string, string&gt; { ["Authorization"] = "ApiKey base64value" }</c>.
	/// </summary>
	public Dictionary<string, string>? Headers { get; init; }

	/// <summary> Socket read timeout for the remote connection (e.g. <c>"1m"</c>). Defaults to 30s. </summary>
	public string? SocketTimeout { get; init; }

	/// <summary> Connection timeout for the remote cluster (e.g. <c>"30s"</c>). Defaults to 30s. </summary>
	public string? ConnectTimeout { get; init; }
}
