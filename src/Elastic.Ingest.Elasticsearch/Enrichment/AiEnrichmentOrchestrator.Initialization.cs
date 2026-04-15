// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Enrichment;

public partial class AiEnrichmentOrchestrator
{
	private async Task EnsureLookupIndexAsync(CancellationToken ct)
	{
		var exists = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.HEAD, _infra.LookupIndexName, cancellationToken: ct).ConfigureAwait(false);

		if (exists.ApiCallDetails.HttpStatusCode == 200)
			return;

		var put = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.PUT, _infra.LookupIndexName,
			PostData.String(_infra.LookupIndexMapping), cancellationToken: ct).ConfigureAwait(false);

		if (put.ApiCallDetails.HttpStatusCode is not (200 or 201))
			throw new Exception($"Failed to create lookup index '{_infra.LookupIndexName}': HTTP {put.ApiCallDetails.HttpStatusCode}");
	}

	/// <returns><c>true</c> if a new policy was created; <c>false</c> if it already existed.</returns>
	private async Task<bool> EnsureEnrichPolicyAsync(CancellationToken ct)
	{
		// Policy name is hash-versioned (e.g. my-cache-ai-policy-a1b2c3d4),
		// so existence == current schema.
		var exists = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.GET, $"_enrich/policy/{_versionedPolicyName}",
			cancellationToken: ct).ConfigureAwait(false);

		if (exists.ApiCallDetails.HttpStatusCode == 200)
			return false;

		var put = await _transport.RequestAsync<StringResponse>(
			HttpMethod.PUT, $"_enrich/policy/{_versionedPolicyName}",
			PostData.String(_infra.EnrichPolicyBody), cancellationToken: ct).ConfigureAwait(false);

		if (put.ApiCallDetails.HttpStatusCode is not (200 or 201))
			throw new Exception(
				$"Failed to create enrich policy '{_versionedPolicyName}': " +
				$"HTTP {put.ApiCallDetails.HttpStatusCode} — {put.Body}");

		return true;
	}

	private async Task ExecuteEnrichPolicyAsync(CancellationToken ct)
	{
		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST, $"_enrich/policy/{_versionedPolicyName}/_execute",
			PostData.Empty, cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception($"Failed to execute enrich policy '{_versionedPolicyName}': HTTP {response.ApiCallDetails.HttpStatusCode}");
	}

	private async Task EnsurePipelineAsync(CancellationToken ct)
	{
		var expectedTag = $"[fields_hash:{_infra.FieldsHash}]";

		var existing = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.GET, $"_ingest/pipeline/{_infra.PipelineName}",
			cancellationToken: ct).ConfigureAwait(false);

		if (existing.ApiCallDetails.HttpStatusCode == 200 && existing.Body is JsonObject pipelineRoot)
		{
			var desc = pipelineRoot[_infra.PipelineName]?["description"]?.GetValue<string>();
			if (desc != null && desc.Contains(expectedTag))
				return;
		}

		// Replace the base policy name with the versioned name in the pipeline body
		// so the enrich processor references the hash-versioned policy.
		var pipelineBody = _infra.PipelineBody.Replace(_infra.EnrichPolicyName, _versionedPolicyName);

		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.PUT, $"_ingest/pipeline/{_infra.PipelineName}",
			PostData.String(pipelineBody), cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception($"Failed to create pipeline '{_infra.PipelineName}': HTTP {response.ApiCallDetails.HttpStatusCode}");
	}

	private async Task CleanupStalePoliciesAsync(CancellationToken ct)
	{
		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.GET, "_enrich/policy",
			cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode != 200 || response.Body is not JsonObject root)
			return;

		var policies = root["policies"]?.AsArray();
		if (policies == null || policies.Count == 0)
			return;

		foreach (var policy in policies)
		{
			var match = policy?["config"]?["match"];
			if (match == null)
				continue;

			var name = match["name"]?.GetValue<string>();
			if (name == null || name == _versionedPolicyName)
				continue;

			// Only delete policies owned by this orchestrator (same base name prefix)
			if (!name.StartsWith(_infra.EnrichPolicyName, StringComparison.Ordinal))
				continue;

			var indicesNode = match["indices"];
			if (indicesNode == null)
				continue;

			var indexName = indicesNode is JsonArray arr
				? arr.Count == 1 ? arr[0]?.GetValue<string>() : null
				: indicesNode.GetValue<string>();

			if (indexName != _infra.LookupIndexName)
				continue;

			// Stale policy referencing our lookup index — best-effort delete
			await _transport.RequestAsync<StringResponse>(
				HttpMethod.DELETE, $"_enrich/policy/{name}",
				cancellationToken: ct).ConfigureAwait(false);
		}
	}
}
