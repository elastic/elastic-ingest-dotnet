// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Queries;

/// <summary>
/// Executes a _delete_by_query operation with wait_for_completion=false and monitors progress.
/// </summary>
public sealed class DeleteByQuery
{
	private readonly ITransport _transport;
	private readonly DeleteByQueryOptions _options;
	private readonly string _index;

	/// <summary>
	/// Creates a new delete-by-query operation.
	/// </summary>
	public DeleteByQuery(ITransport transport, DeleteByQueryOptions options)
	{
		_transport = transport;
		_options = options;
		_index = options.Index
			?? (options.TypeContext != null ? TypeContextResolver.ResolveWriteAlias(options.TypeContext) : null)
			?? throw new InvalidOperationException("Either Index or TypeContext must be provided on DeleteByQueryOptions.");
	}

	/// <summary>
	/// Starts _delete_by_query with wait_for_completion=false, then polls _tasks/{id}.
	/// Yields progress on each poll. Completes when the task finishes.
	/// </summary>
	public async IAsyncEnumerable<DeleteByQueryProgress> MonitorAsync([EnumeratorCancellation] CancellationToken ctx = default)
	{
		var taskId = await StartDeleteByQueryAsync(ctx).ConfigureAwait(false);

		await foreach (var response in ElasticsearchTaskMonitor.PollTaskAsync(_transport, taskId, _options.PollInterval, ctx).ConfigureAwait(false))
		{
			yield return ParseProgress(taskId, response);
		}
	}

	/// <summary>
	/// Convenience: runs to completion and returns the final progress.
	/// </summary>
	public async Task<DeleteByQueryProgress> RunAsync(CancellationToken ctx = default)
	{
		DeleteByQueryProgress? last = null;
		await foreach (var progress in MonitorAsync(ctx).ConfigureAwait(false))
			last = progress;
		return last ?? new DeleteByQueryProgress { IsCompleted = true };
	}

	private async Task<string> StartDeleteByQueryAsync(CancellationToken ctx)
	{
		var url = BuildUrl();
		var body = $@"{{""query"":{_options.QueryBody}}}";

		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST, url, PostData.String(body), cancellationToken: ctx
		).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception(
				$"Failed to start _delete_by_query on '{_index}': {response}",
				response.ApiCallDetails.OriginalException
			);

		return response.Get<string>("task")
			?? throw new Exception("Delete by query response did not contain a 'task' field.");
	}

	private string BuildUrl()
	{
		var sb = new StringBuilder("/");
		sb.Append(_index).Append("/_delete_by_query?wait_for_completion=false");
		if (_options.RequestsPerSecond.HasValue)
			sb.Append("&requests_per_second=").Append(_options.RequestsPerSecond.Value);
		if (_options.Slices != null)
			sb.Append("&slices=").Append(_options.Slices);
		return sb.ToString();
	}

	private static DeleteByQueryProgress ParseProgress(string taskId, JsonResponse response)
	{
		var completed = response.Get<bool>("completed");
		var error = response.Get<string>("error.reason");

		if (completed)
		{
			var respError = response.Get<string>("response.failures");
			if (!string.IsNullOrEmpty(respError))
				error ??= respError;
		}

		return new DeleteByQueryProgress
		{
			TaskId = taskId,
			IsCompleted = completed,
			Total = response.Get<long>("task.status.total"),
			Deleted = response.Get<long>("task.status.deleted"),
			VersionConflicts = response.Get<long>("task.status.version_conflicts"),
			Elapsed = TimeSpan.FromMilliseconds(response.Get<long>("task.running_time_in_nanos") / 1_000_000.0),
			Error = error
		};
	}
}
