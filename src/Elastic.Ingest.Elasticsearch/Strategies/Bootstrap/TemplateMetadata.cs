// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Parsed <c>_meta</c> fields from an existing index template.
/// </summary>
public record TemplateMetadata(string? Hash, string? MappingVersion)
{
	/// <summary> An empty metadata instance (no template found or no _meta). </summary>
	public static readonly TemplateMetadata Empty = new(null, null);
}

/// <summary>
/// Shared helpers for reading index template <c>_meta</c> and deciding whether bootstrap should proceed.
/// </summary>
public static class TemplateMetadataHelper
{
	private const string FilterPath = "index_templates.index_template._meta.hash,index_templates.index_template._meta.mapping_version";

	/// <summary>
	/// Fetches the <c>_meta</c> from an existing index template (async).
	/// </summary>
	public static async Task<TemplateMetadata> FetchMetaAsync(
		ITransport transport, string templateName, CancellationToken ctx = default)
	{
		var response = await transport.RequestAsync<StringResponse>(
			HttpMethod.GET, $"/_index_template/{templateName}?filter_path={FilterPath}", ctx
		).ConfigureAwait(false);

		if (!response.ApiCallDetails.HasSuccessfulStatusCode || string.IsNullOrEmpty(response.Body))
			return TemplateMetadata.Empty;

		return ReadMeta(response.Body);
	}

	/// <summary>
	/// Fetches the <c>_meta</c> from an existing index template (sync).
	/// </summary>
	public static TemplateMetadata FetchMeta(ITransport transport, string templateName)
	{
		var response = transport.Request<StringResponse>(
			HttpMethod.GET, $"/_index_template/{templateName}?filter_path={FilterPath}"
		);

		if (!response.ApiCallDetails.HasSuccessfulStatusCode || string.IsNullOrEmpty(response.Body))
			return TemplateMetadata.Empty;

		return ReadMeta(response.Body);
	}

	/// <summary>
	/// Parses <c>hash</c> and <c>mapping_version</c> from the filtered <c>_meta</c> response body.
	/// Expected shape: <c>{"index_templates":[{"index_template":{"_meta":{"hash":"...","mapping_version":"..."}}}]}</c>
	/// </summary>
	public static TemplateMetadata ReadMeta(string responseBody)
	{
		// Strip the envelope to get the _meta object contents
		const string prefix = """{"index_templates":[{"index_template":{"_meta":{""";
		if (!responseBody.StartsWith(prefix, StringComparison.Ordinal))
			return TemplateMetadata.Empty;

		var inner = responseBody.Substring(prefix.Length).TrimEnd('}', ']');
		// inner is now something like: "hash":"abc123","mapping_version":"1.0.0"
		// or just "hash":"abc123"

		var hash = ExtractJsonStringValue(inner, "hash");
		var mappingVersion = ExtractJsonStringValue(inner, "mapping_version");

		return new TemplateMetadata(hash, mappingVersion);
	}

	/// <summary>
	/// Determines whether bootstrap should be skipped based on remote metadata and local state.
	/// <para>
	/// Bootstrap is skipped (returns <see langword="true"/>) when <b>either</b> condition is met:
	/// </para>
	/// <list type="number">
	/// <item><b>Version guard</b>: both local and remote have a parseable <c>mapping_version</c>
	/// and remote is strictly greater than local — the cluster already has a newer deployment's
	/// templates, so the older pod must not overwrite them.</item>
	/// <item><b>Hash check</b>: the content hashes match — templates are identical, nothing to do.</item>
	/// </list>
	/// <para>
	/// Bootstrap proceeds (returns <see langword="false"/>) only when the hash differs <b>and</b>
	/// the remote version is not newer than the local version.
	/// </para>
	/// <para>
	/// When <paramref name="localMappingVersion"/> is <see langword="null"/>, only the hash check
	/// applies — the original hash-only behavior.
	/// </para>
	/// </summary>
	public static bool ShouldSkipBootstrap(
		TemplateMetadata remote, string localHash, string? localMappingVersion)
	{
		// Version guard: if both sides have a parseable mapping_version and remote is newer, skip
		if (localMappingVersion != null
			&& remote.MappingVersion != null
			&& Version.TryParse(remote.MappingVersion, out var remoteVersion)
			&& Version.TryParse(localMappingVersion, out var localVersion)
			&& remoteVersion > localVersion)
		{
			return true;
		}

		// Hash check: if hashes match, nothing changed
		if (!string.IsNullOrWhiteSpace(remote.Hash) && remote.Hash == localHash)
			return true;

		return false;
	}

	/// <summary>
	/// Builds the optional <c>mapping_version</c> JSON fragment for inclusion in <c>_meta</c>.
	/// Returns empty string when <paramref name="mappingVersion"/> is null.
	/// </summary>
	public static string BuildMappingVersionFragment(string? mappingVersion) =>
		mappingVersion != null
			? $@",
                    ""mapping_version"": ""{mappingVersion}"""
			: string.Empty;

	private static string? ExtractJsonStringValue(string json, string key)
	{
		var marker = $"\"{key}\":\"";
		var start = json.IndexOf(marker, StringComparison.Ordinal);
		if (start < 0)
			return null;

		start += marker.Length;
		var end = json.IndexOf('"', start);
		return end < 0 ? null : json.Substring(start, end - start);
	}
}
