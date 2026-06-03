// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Helpers;

/// <summary>
/// Provides access to the reindex-specific management APIs introduced in Elasticsearch 9.5.0.
/// <para>
/// <c>GET /_reindex</c> — list all running reindex operations.<br/>
/// <c>GET /_reindex/{taskId}</c> — get status for one operation (relocation-aware).<br/>
/// <c>POST /_reindex/{taskId}/_cancel</c> — cancel an operation (relocation-aware).<br/>
/// <c>POST /_reindex/{taskId}/_rethrottle</c> — change throttle on an in-flight operation.
/// </para>
/// <para>
/// <see cref="GetStatusAsync"/> and <see cref="CancelAsync"/> fall back to the legacy
/// <c>/_tasks</c> API when the reindex management endpoints are not available (older clusters).
/// <see cref="ListAsync"/> is new-only — there is no legacy equivalent.
/// </para>
/// </summary>
public sealed class ReindexOperations
{
	private readonly ITransport _transport;

	/// <summary>
	/// Creates a new <see cref="ReindexOperations"/> instance.
	/// </summary>
	public ReindexOperations(ITransport transport) => _transport = transport;

	/// <summary>
	/// Lists all currently running reindex operations.
	/// <para>Calls <c>GET /_reindex</c>. Requires Elasticsearch 9.5.0+ or Serverless.</para>
	/// </summary>
	/// <param name="detailed">When <c>true</c>, includes detailed task status in each entry.</param>
	/// <param name="ctx">Cancellation token.</param>
	/// <returns>The raw JSON response containing a <c>reindex</c> array.</returns>
	public async Task<JsonResponse> ListAsync(bool detailed = false, CancellationToken ctx = default)
	{
		var url = detailed ? "/_reindex?detailed=true" : "/_reindex";
		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.GET, url, cancellationToken: ctx
		).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception(
				$"Failed to list reindex operations: {response}",
				response.ApiCallDetails.OriginalException
			);

		return response;
	}

	/// <summary>
	/// Gets the status and progress of a specific reindex task.
	/// Tries <c>GET /_reindex/{taskId}</c> first, falls back to <c>GET /_tasks/{taskId}</c>.
	/// </summary>
	public async Task<ReindexProgress> GetStatusAsync(string taskId, CancellationToken ctx = default)
	{
		var result = await ReindexTaskMonitor.GetStatusAsync(_transport, taskId, ctx).ConfigureAwait(false);
		return ServerReindex.ParseProgress(taskId, result.Response, result.Shape);
	}

	/// <summary>
	/// Cancels a running reindex operation.
	/// Tries <c>POST /_reindex/{taskId}/_cancel</c> first (relocation-aware), falls back to
	/// <c>POST /_tasks/{taskId}/_cancel</c>.
	/// </summary>
	/// <param name="taskId">The reindex task identifier.</param>
	/// <param name="waitForCompletion">
	/// When <c>true</c> (default), blocks until cancellation is complete and returns the final task state.
	/// When <c>false</c>, returns immediately after acknowledgement.
	/// </param>
	/// <param name="ctx">Cancellation token.</param>
	/// <returns>
	/// The final <see cref="ReindexProgress"/> when <paramref name="waitForCompletion"/> is <c>true</c>;
	/// <c>null</c> when <c>false</c> (only acknowledgement is returned).
	/// </returns>
	public async Task<ReindexProgress?> CancelAsync(
		string taskId, bool waitForCompletion = true, CancellationToken ctx = default)
	{
		var result = await TryCancelReindexApiAsync(taskId, waitForCompletion, ctx).ConfigureAwait(false);
		if (result.HasValue)
		{
			var (response, shape) = result.Value;
			if (!waitForCompletion)
				return null;
			return ServerReindex.ParseProgress(taskId, response, shape);
		}

		// Fallback to legacy task cancel
		var fallback = await CancelViaTaskApiAsync(taskId, ctx).ConfigureAwait(false);
		return waitForCompletion
			? ServerReindex.ParseProgress(taskId, fallback, ReindexTaskMonitor.ApiShape.TaskApi)
			: null;
	}

	/// <summary>
	/// Changes the throttle on an in-flight reindex operation.
	/// <para>Calls <c>POST /_reindex/{taskId}/_rethrottle?requests_per_second={rps}</c>.</para>
	/// </summary>
	/// <param name="taskId">The reindex task identifier.</param>
	/// <param name="requestsPerSecond">
	/// The new throttle value. Use <c>-1</c> to disable throttling.
	/// </param>
	/// <param name="ctx">Cancellation token.</param>
	public async Task<JsonResponse> RethrottleAsync(
		string taskId, float requestsPerSecond, CancellationToken ctx = default)
	{
		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST,
			$"/_reindex/{taskId}/_rethrottle?requests_per_second={requestsPerSecond}",
			cancellationToken: ctx
		).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception(
				$"Failed to rethrottle reindex task '{taskId}': {response}",
				response.ApiCallDetails.OriginalException
			);

		return response;
	}

	private async Task<(JsonResponse Response, ReindexTaskMonitor.ApiShape Shape)?> TryCancelReindexApiAsync(
		string taskId, bool waitForCompletion, CancellationToken ctx)
	{
		var wfc = waitForCompletion ? "true" : "false";
		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST,
			$"/_reindex/{taskId}/_cancel?wait_for_completion={wfc}",
			cancellationToken: ctx
		).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is 200)
			return (response, ReindexTaskMonitor.ApiShape.ReindexApi);

		// 503 during relocation handoff — retry once
		if (response.ApiCallDetails.HttpStatusCode is 503)
		{
			await Task.Delay(TimeSpan.FromSeconds(2), ctx).ConfigureAwait(false);
			var retry = await _transport.RequestAsync<JsonResponse>(
				HttpMethod.POST,
				$"/_reindex/{taskId}/_cancel?wait_for_completion={wfc}",
				cancellationToken: ctx
			).ConfigureAwait(false);

			if (retry.ApiCallDetails.HttpStatusCode is 200)
				return (retry, ReindexTaskMonitor.ApiShape.ReindexApi);
		}

		return null;
	}

	private async Task<JsonResponse> CancelViaTaskApiAsync(string taskId, CancellationToken ctx)
	{
		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST,
			$"/_tasks/{taskId}/_cancel",
			cancellationToken: ctx
		).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception(
				$"Failed to cancel task '{taskId}': {response}",
				response.ApiCallDetails.OriginalException
			);

		return response;
	}
}
