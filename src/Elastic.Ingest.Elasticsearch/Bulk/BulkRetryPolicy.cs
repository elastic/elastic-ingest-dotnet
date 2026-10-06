// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using Elastic.Ingest.Elasticsearch.Serialization;

namespace Elastic.Ingest.Elasticsearch.Bulk;

/// <summary>
/// Controls if and how a <see cref="BulkSender{TItem,TBody}"/> retries failed items.
/// Only the items that <see cref="IsRetryable"/> accepts are re-sent, the rest keep their result.
/// </summary>
public sealed record BulkRetryPolicy
{
	/// <summary>Exactly one request, no retries. Callers own retry and backoff.</summary>
	public static BulkRetryPolicy None { get; } = new() { MaxRetries = 0 };

	/// <summary>Three retries of items that failed with 429, 502, 503 or 504, backing off two seconds.</summary>
	public static BulkRetryPolicy Default { get; } = new() { MaxRetries = 3 };

	/// <summary>The maximum number of times failed items are re-sent.</summary>
	public int MaxRetries { get; init; }

	/// <summary>Delay before retry attempt <c>n</c> (starting at 0). Defaults to two seconds.</summary>
	public Func<int, TimeSpan> Backoff { get; init; } = static _ => TimeSpan.FromSeconds(2);

	/// <summary>Decides if a failed item should be re-sent. Defaults to the 429, 502, 503 and 504 status codes.</summary>
	public Func<BulkResponseItem, bool> IsRetryable { get; init; } = static item => IngestChannelStatics.RetryStatusCodes.Contains(item.Status);

	/// <summary>Re-send the whole request when the HTTP response itself is a 429. Defaults to <c>true</c>.</summary>
	public bool RetryAllOnHttp429 { get; init; } = true;
}
