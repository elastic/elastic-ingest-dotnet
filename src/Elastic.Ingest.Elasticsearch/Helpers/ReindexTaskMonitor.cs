// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Helpers;

/// <summary>
/// Polls reindex task status using the dedicated <c>GET /_reindex/{taskId}</c> API (9.5.0+),
/// falling back to <c>GET /_tasks/{taskId}</c> on older clusters.
/// </summary>
internal static class ReindexTaskMonitor
{
	/// <summary>
	/// Indicates which API shape a <see cref="JsonResponse"/> was obtained from.
	/// </summary>
	internal enum ApiShape { ReindexApi, TaskApi }

	internal readonly record struct PollResult(JsonResponse Response, ApiShape Shape);

	/// <summary>
	/// Polls the reindex task at the given interval, yielding <see cref="PollResult"/> on each poll.
	/// Tries the reindex management API first; if the endpoint is unavailable, falls back to the
	/// task API for the remainder of the operation.
	/// </summary>
	internal static async IAsyncEnumerable<PollResult> PollAsync(
		ITransport transport,
		string taskId,
		TimeSpan pollInterval,
		[EnumeratorCancellation] CancellationToken ctx = default)
	{
		var useReindexApi = true;

		while (!ctx.IsCancellationRequested)
		{
			PollResult result;
			if (useReindexApi)
			{
				result = await TryReindexApiAsync(transport, taskId, ctx).ConfigureAwait(false);
				if (result.Shape == ApiShape.TaskApi)
					useReindexApi = false;
			}
			else
			{
				var response = await FetchTaskApiAsync(transport, taskId, ctx).ConfigureAwait(false);
				result = new PollResult(response, ApiShape.TaskApi);
			}

			yield return result;

			var completed = result.Shape == ApiShape.ReindexApi
				? result.Response.Get<bool>("completed")
				: result.Response.Get<bool>("completed");
			if (completed)
				yield break;

			await Task.Delay(pollInterval, ctx).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Single-shot status fetch via the reindex management API with fallback.
	/// </summary>
	internal static async Task<PollResult> GetStatusAsync(
		ITransport transport, string taskId, CancellationToken ctx = default)
	{
		var result = await TryReindexApiAsync(transport, taskId, ctx).ConfigureAwait(false);
		return result;
	}

	private static async Task<PollResult> TryReindexApiAsync(
		ITransport transport, string taskId, CancellationToken ctx)
	{
		var response = await transport.RequestAsync<JsonResponse>(
			HttpMethod.GET,
			$"/_reindex/{taskId}",
			cancellationToken: ctx
		).ConfigureAwait(false);

		var status = response.ApiCallDetails.HttpStatusCode;

		// 200 = success on reindex management API
		if (status is 200)
			return new PollResult(response, ApiShape.ReindexApi);

		// 404 with "resource_not_found_exception" could mean the task genuinely doesn't exist
		// on the new API, OR it could mean the endpoint itself doesn't exist on an older cluster.
		// 405 (Method Not Allowed) definitively means the endpoint doesn't exist.
		// In either ambiguous case, try the task API as fallback.
		var fallback = await FetchTaskApiAsync(transport, taskId, ctx).ConfigureAwait(false);
		return new PollResult(fallback, ApiShape.TaskApi);
	}

	private static async Task<JsonResponse> FetchTaskApiAsync(
		ITransport transport, string taskId, CancellationToken ctx)
	{
		var response = await transport.RequestAsync<JsonResponse>(
			HttpMethod.GET,
			$"/_tasks/{taskId}",
			cancellationToken: ctx
		).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception(
				$"Failed to get task status for '{taskId}': {response}",
				response.ApiCallDetails.OriginalException
			);

		return response;
	}
}
