// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
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

	private async Task EnsureEnrichPolicyAsync(CancellationToken ct)
	{
		var exists = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.GET, $"_enrich/policy/{_infra.EnrichPolicyName}",
			cancellationToken: ct).ConfigureAwait(false);

		if (exists.ApiCallDetails.HttpStatusCode == 200)
		{
			var matchResult = PolicyMatchesCurrentFields(exists);
			if (matchResult == PolicyMatch.Matches)
				return;

			if (matchResult == PolicyMatch.SchemaChanged)
			{
				await _transport.RequestAsync<StringResponse>(
					HttpMethod.DELETE, $"_ingest/pipeline/{_infra.PipelineName}",
					cancellationToken: ct).ConfigureAwait(false);

				var del = await _transport.RequestAsync<StringResponse>(
					HttpMethod.DELETE, $"_enrich/policy/{_infra.EnrichPolicyName}",
					cancellationToken: ct).ConfigureAwait(false);

				if (del.ApiCallDetails.HttpStatusCode is not (200 or 404))
					throw new Exception(
						$"Failed to delete stale enrich policy '{_infra.EnrichPolicyName}': " +
						$"HTTP {del.ApiCallDetails.HttpStatusCode} — {del.Body}");
			}
		}

		var put = await _transport.RequestAsync<StringResponse>(
			HttpMethod.PUT, $"_enrich/policy/{_infra.EnrichPolicyName}",
			PostData.String(_infra.EnrichPolicyBody), cancellationToken: ct).ConfigureAwait(false);

		if (put.ApiCallDetails.HttpStatusCode is not (200 or 201))
			throw new Exception(
				$"Failed to create enrich policy '{_infra.EnrichPolicyName}': " +
				$"HTTP {put.ApiCallDetails.HttpStatusCode} — {put.Body}");
	}

	private enum PolicyMatch { NotFound, Matches, SchemaChanged }

	private PolicyMatch PolicyMatchesCurrentFields(JsonResponse response)
	{
		if (response.Body is not JsonObject root)
			return PolicyMatch.NotFound;

		var policies = root["policies"]?.AsArray();
		if (policies == null || policies.Count == 0)
			return PolicyMatch.NotFound;

		foreach (var policy in policies)
		{
			var match = policy?["config"]?["match"];
			if (match == null)
				continue;

			var indicesNode = match["indices"];
			if (indicesNode != null)
			{
				var indexName = indicesNode is JsonArray arr
					? arr.Count == 1 ? arr[0]?.GetValue<string>() : null
					: indicesNode.GetValue<string>();

				if (indexName != _infra.LookupIndexName)
					return PolicyMatch.SchemaChanged;
			}

			var fields = match["enrich_fields"]?.AsArray();
			if (fields == null)
				continue;

			var existingFields = new HashSet<string>();
			foreach (var f in fields)
			{
				if (f?.GetValue<string>() is { } s)
					existingFields.Add(s);
			}

			var expectedFields = new HashSet<string>();
			foreach (var field in _provider.EnrichmentFields)
			{
				expectedFields.Add(field);
				if (_provider.FieldPromptHashFieldNames.TryGetValue(field, out var phField))
					expectedFields.Add(phField);
			}

			return existingFields.SetEquals(expectedFields)
				? PolicyMatch.Matches
				: PolicyMatch.SchemaChanged;
		}

		return PolicyMatch.NotFound;
	}

	private async Task ExecuteEnrichPolicyAsync(CancellationToken ct)
	{
		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST, $"_enrich/policy/{_infra.EnrichPolicyName}/_execute",
			PostData.Empty, cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception($"Failed to execute enrich policy '{_infra.EnrichPolicyName}': HTTP {response.ApiCallDetails.HttpStatusCode}");
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

		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.PUT, $"_ingest/pipeline/{_infra.PipelineName}",
			PostData.String(_infra.PipelineBody), cancellationToken: ct).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception($"Failed to create pipeline '{_infra.PipelineName}': HTTP {response.ApiCallDetails.HttpStatusCode}");
	}
}
