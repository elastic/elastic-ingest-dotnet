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

namespace Elastic.Ingest.Elasticsearch.Reindex;

/// <summary>
/// Executes a server-side _reindex operation with wait_for_completion=false and monitors progress.
/// </summary>
public sealed class ServerReindex
{
	private readonly ITransport _transport;
	private readonly ServerReindexOptions _options;
	private readonly string _source;
	private readonly string _destination;

	/// <summary>
	/// Creates a new server-side reindex operation.
	/// </summary>
	public ServerReindex(ITransport transport, ServerReindexOptions options)
	{
		_transport = transport;
		_options = options;

		if (options.Body == null)
		{
			_source = options.Source
				?? (options.SourceContext != null ? TypeContextResolver.ResolveWriteAlias(options.SourceContext) : null)
				?? throw new InvalidOperationException("Either Source or SourceContext must be provided on ServerReindexOptions when Body is not set.");
			_destination = options.Destination
				?? (options.DestinationContext != null ? TypeContextResolver.ResolveWriteAlias(options.DestinationContext) : null)
				?? throw new InvalidOperationException("Either Destination or DestinationContext must be provided on ServerReindexOptions when Body is not set.");
		}
		else
		{
			_source = options.Source ?? string.Empty;
			_destination = options.Destination ?? string.Empty;
		}
	}

	/// <summary>
	/// Starts _reindex with wait_for_completion=false, then polls _tasks/{id}.
	/// Yields progress on each poll. Completes when the task finishes.
	/// </summary>
	public async IAsyncEnumerable<ReindexProgress> MonitorAsync([EnumeratorCancellation] CancellationToken ctx = default)
	{
		var taskId = await StartReindexAsync(ctx).ConfigureAwait(false);

		await foreach (var response in ElasticsearchTaskMonitor.PollTaskAsync(_transport, taskId, _options.PollInterval, ctx).ConfigureAwait(false))
		{
			yield return ParseProgress(taskId, response);
		}
	}

	/// <summary>
	/// Convenience: runs to completion and returns the final progress.
	/// </summary>
	public async Task<ReindexProgress> RunAsync(CancellationToken ctx = default)
	{
		ReindexProgress? last = null;
		await foreach (var progress in MonitorAsync(ctx).ConfigureAwait(false))
			last = progress;
		return last ?? new ReindexProgress { IsCompleted = true };
	}

	private async Task<string> StartReindexAsync(CancellationToken ctx)
	{
		var url = BuildUrl();
		var body = BuildBody();

		var response = await _transport.RequestAsync<JsonResponse>(
			HttpMethod.POST, url, PostData.String(body), cancellationToken: ctx
		).ConfigureAwait(false);

		if (response.ApiCallDetails.HttpStatusCode is not 200)
			throw new Exception(
				$"Failed to start _reindex: {response}",
				response.ApiCallDetails.OriginalException
			);

		return response.Get<string>("task")
			?? throw new Exception("Reindex response did not contain a 'task' field.");
	}

	private string BuildUrl()
	{
		var sb = new StringBuilder("/_reindex?wait_for_completion=false");
		if (_options.RequestsPerSecond.HasValue)
			sb.Append("&requests_per_second=").Append(_options.RequestsPerSecond.Value);
		if (_options.Slices != null)
			sb.Append("&slices=").Append(_options.Slices);
		return sb.ToString();
	}

	private string BuildBody()
	{
		if (_options.Body != null)
			return _options.Body;

		var sb = new StringBuilder(256);
		sb.Append(@"{""source"":{""index"":""");
		sb.Append(_source);
		sb.Append('"');
		if (_options.Query != null)
		{
			sb.Append(@",""query"":");
			sb.Append(_options.Query);
		}
		sb.Append(@"},""dest"":{""index"":""");
		sb.Append(_destination);
		sb.Append('"');
		if (_options.Pipeline != null)
		{
			sb.Append(@",""pipeline"":""");
			sb.Append(_options.Pipeline);
			sb.Append('"');
		}
		sb.Append(@"}}");
		return sb.ToString();
	}

	private static ReindexProgress ParseProgress(string taskId, JsonResponse response)
	{
		var completed = response.Get<bool>("completed");
		var error = response.Get<string>("error.reason");

		if (completed)
		{
			var respError = response.Get<string>("response.failures");
			if (!string.IsNullOrEmpty(respError))
				error ??= respError;
		}

		return new ReindexProgress
		{
			TaskId = taskId,
			IsCompleted = completed,
			Total = response.Get<long>("task.status.total"),
			Created = response.Get<long>("task.status.created"),
			Updated = response.Get<long>("task.status.updated"),
			Deleted = response.Get<long>("task.status.deleted"),
			Noops = response.Get<long>("task.status.noops"),
			VersionConflicts = response.Get<long>("task.status.version_conflicts"),
			Elapsed = TimeSpan.FromMilliseconds(response.Get<long>("task.running_time_in_nanos") / 1_000_000.0),
			Error = error
		};
	}
}
