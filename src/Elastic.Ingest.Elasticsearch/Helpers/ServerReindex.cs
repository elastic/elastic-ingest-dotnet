// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Helpers;

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
				?? (options.SourceContext != null ? options.SourceContext.ResolveWriteAlias() : null)
				?? throw new InvalidOperationException("Either Source or SourceContext must be provided on ServerReindexOptions when Body is not set.");
			_destination = options.Destination
				?? (options.DestinationContext != null ? options.DestinationContext.ResolveWriteAlias() : null)
				?? throw new InvalidOperationException("Either Destination or DestinationContext must be provided on ServerReindexOptions when Body is not set.");
		}
		else
		{
			_source = options.Source ?? string.Empty;
			_destination = options.Destination ?? string.Empty;
		}
	}

	/// <summary>
	/// Starts _reindex with wait_for_completion=false, then polls <c>GET /_reindex/{id}</c>
	/// (falling back to <c>GET /_tasks/{id}</c> on older clusters).
	/// Yields progress on each poll. Completes when the task finishes.
	/// </summary>
	public async IAsyncEnumerable<ReindexProgress> MonitorAsync([EnumeratorCancellation] CancellationToken ctx = default)
	{
		var taskId = await StartReindexAsync(ctx).ConfigureAwait(false);

		await foreach (var result in ReindexTaskMonitor.PollAsync(_transport, taskId, _options.PollInterval, ctx).ConfigureAwait(false))
		{
			yield return ParseProgress(taskId, result.Response, result.Shape);
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
		if (_options.MaxDocs.HasValue)
			sb.Append("&max_docs=").Append(_options.MaxDocs.Value);
		return sb.ToString();
	}

	/// <summary>
	/// Builds the JSON request body that will be sent to <c>POST /_reindex</c>.
	/// Useful for inspecting or logging the body before execution.
	/// </summary>
	public string BuildBody()
	{
		if (_options.Body != null)
			return _options.Body;

		var sb = new StringBuilder(256);
		if (_options.Conflicts != null)
		{
			sb.Append(@"{""conflicts"":""");
			sb.Append(_options.Conflicts);
			sb.Append(@""",""source"":{""index"":""");
		}
		else
			sb.Append(@"{""source"":{""index"":""");
		sb.Append(_source);
		sb.Append('"');
		if (_options.Remote != null)
			AppendRemote(sb, _options.Remote);
		if (_options.SourceSize.HasValue)
		{
			sb.Append(@",""size"":");
			sb.Append(_options.SourceSize.Value);
		}
		if (_options.ExcludeInferenceFields)
			sb.Append(@",""_source"":{""excludes"":[""_inference_fields""]}");
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
		sb.Append('}');
		if (_options.Script != null)
		{
			sb.Append(@",""script"":");
			sb.Append(_options.Script);
		}
		sb.Append('}');
		return sb.ToString();
	}

	private static void AppendRemote(StringBuilder sb, RemoteSource remote)
	{
		sb.Append(@",""remote"":{""host"":""");
		sb.Append(remote.Host);
		sb.Append('"');
		if (remote.Username != null)
		{
			sb.Append(@",""username"":""");
			sb.Append(remote.Username);
			sb.Append('"');
		}
		if (remote.Password != null)
		{
			sb.Append(@",""password"":""");
			sb.Append(remote.Password);
			sb.Append('"');
		}
		if (remote.ApiKey != null)
		{
			sb.Append(@",""api_key"":""");
			sb.Append(remote.ApiKey);
			sb.Append('"');
		}
		if (remote.Headers is { Count: > 0 })
		{
			sb.Append(@",""headers"":{");
			var first = true;
			foreach (var kvp in remote.Headers)
			{
				if (!first) sb.Append(',');
				sb.Append('"').Append(kvp.Key).Append(@""":""").Append(kvp.Value).Append('"');
				first = false;
			}
			sb.Append('}');
		}
		if (remote.SocketTimeout != null)
		{
			sb.Append(@",""socket_timeout"":""");
			sb.Append(remote.SocketTimeout);
			sb.Append('"');
		}
		if (remote.ConnectTimeout != null)
		{
			sb.Append(@",""connect_timeout"":""");
			sb.Append(remote.ConnectTimeout);
			sb.Append('"');
		}
		sb.Append('}');
	}

	/// <summary>
	/// Parses a reindex status <see cref="JsonResponse"/> into a <see cref="ReindexProgress"/> snapshot.
	/// </summary>
	/// <param name="taskId">The task identifier.</param>
	/// <param name="response">The raw JSON response from Elasticsearch.</param>
	/// <param name="isReindexApi">
	/// <c>true</c> for the <c>GET /_reindex/{id}</c> shape (9.5.0+);
	/// <c>false</c> for the legacy <c>GET /_tasks/{id}</c> shape.
	/// </param>
	public static ReindexProgress ParseProgress(
		string taskId, JsonResponse response, bool isReindexApi)
	{
		return isReindexApi
			? ParseReindexApiProgress(taskId, response)
			: ParseTaskApiProgress(taskId, response);
	}

	internal static ReindexProgress ParseProgress(
		string taskId, JsonResponse response, ReindexTaskMonitor.ApiShape shape) =>
		ParseProgress(taskId, response, shape == ReindexTaskMonitor.ApiShape.ReindexApi);

	/// <summary>
	/// Parses the <c>GET /_reindex/{id}</c> response shape (9.5.0+).
	/// Status fields live at <c>status.*</c> and <c>running_time_in_nanos</c> is at the root.
	/// </summary>
	private static ReindexProgress ParseReindexApiProgress(string taskId, JsonResponse response)
	{
		var completed = response.Get<bool>("completed");
		var error = response.Get<string>("error.reason");
		var failures = completed ? ParseFailures(response.Body?["response"]?["failures"]) : [];

		if (failures.Count > 0)
			error ??= $"{failures.Count} document(s) failed: {failures[0].CauseReason ?? failures[0].CauseType ?? "unknown"}";

		var id = response.Get<string>("id") ?? taskId;
		var startMillis = response.Get<long>("start_time_in_millis");
		DateTimeOffset? startTime = startMillis > 0
			? DateTimeOffset.FromUnixTimeMilliseconds(startMillis)
			: null;

		return new ReindexProgress
		{
			TaskId = id,
			IsCompleted = completed,
			Cancelled = response.Get<bool>("cancelled"),
			Total = response.Get<long>("status.total"),
			Created = response.Get<long>("status.created"),
			Updated = response.Get<long>("status.updated"),
			Deleted = response.Get<long>("status.deleted"),
			Noops = response.Get<long>("status.noops"),
			VersionConflicts = response.Get<long>("status.version_conflicts"),
			Elapsed = TimeSpan.FromMilliseconds(response.Get<long>("running_time_in_nanos") / 1_000_000.0),
			Description = response.Get<string>("description"),
			StartTime = startTime,
			Error = error,
			Failures = failures
		};
	}

	private static IReadOnlyList<ReindexFailure> ParseFailures(JsonNode? failuresNode)
	{
		if (failuresNode is not JsonArray arr || arr.Count == 0)
			return [];

		var list = new List<ReindexFailure>(arr.Count);
		foreach (var item in arr)
		{
			if (item is not JsonObject obj)
				continue;

			var cause = obj["cause"];
			list.Add(new ReindexFailure(
				Index: obj["index"]?.GetValue<string>(),
				Id: obj["id"]?.GetValue<string>(),
				Status: obj["status"]?.GetValue<int>(),
				CauseType: cause?["type"]?.GetValue<string>(),
				CauseReason: cause?["reason"]?.GetValue<string>()
			));
		}
		return list;
	}

	/// <summary>
	/// Parses the legacy <c>GET /_tasks/{id}</c> response shape.
	/// Status fields live at <c>task.status.*</c> and <c>running_time_in_nanos</c> is under <c>task</c>.
	/// </summary>
	private static ReindexProgress ParseTaskApiProgress(string taskId, JsonResponse response)
	{
		var completed = response.Get<bool>("completed");
		var error = response.Get<string>("error.reason");
		var failures = completed ? ParseFailures(response.Body?["response"]?["failures"]) : [];

		if (failures.Count > 0)
			error ??= $"{failures.Count} document(s) failed: {failures[0].CauseReason ?? failures[0].CauseType ?? "unknown"}";

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
			Error = error,
			Failures = failures
		};
	}
}
