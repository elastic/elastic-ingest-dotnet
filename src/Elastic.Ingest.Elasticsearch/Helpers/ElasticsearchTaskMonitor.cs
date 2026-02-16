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

internal static class ElasticsearchTaskMonitor
{
	/// <summary>
	/// Polls GET _tasks/{taskId} at the given interval, yields the <see cref="JsonResponse"/> on each poll.
	/// Completes when the task reports as completed.
	/// </summary>
	internal static async IAsyncEnumerable<JsonResponse> PollTaskAsync(
		ITransport transport,
		string taskId,
		TimeSpan pollInterval,
		[EnumeratorCancellation] CancellationToken ctx = default)
	{
		while (!ctx.IsCancellationRequested)
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

			yield return response;

			if (response.Get<bool>("completed"))
				yield break;

			await Task.Delay(pollInterval, ctx).ConfigureAwait(false);
		}
	}
}
