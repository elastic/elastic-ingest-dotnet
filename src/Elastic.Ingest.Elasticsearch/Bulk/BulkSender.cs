// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Transport;
using static Elastic.Ingest.Elasticsearch.IngestChannelStatics;

// ThrowIfNull is not available on netstandard
#pragma warning disable CA1510

namespace Elastic.Ingest.Elasticsearch.Bulk;

/// <summary>
/// A stateless, thread-safe primitive that sends one <c>_bulk</c> request per call and returns the typed
/// <see cref="BulkResponse"/>. It owns no threads, no queues and needs no disposal.
/// <para>
/// <see cref="BulkResponse.Items"/> always lines up position by position with the items that were sent, also when
/// a <see cref="BulkRetryPolicy"/> re-sent some of them.
/// </para>
/// </summary>
/// <typeparam name="TItem">The item the action and body are derived from.</typeparam>
/// <typeparam name="TBody">The type serialized as the document.</typeparam>
public sealed class BulkSender<TItem, TBody>
{
	private readonly ITransport _transport;
	private readonly Func<TItem, BulkAction> _action;
	private readonly Func<TItem, TBody> _body;
	private readonly JsonTypeInfo<TBody> _typeInfo;
	private readonly BulkRetryPolicy _retry;
	private readonly string _url;

	/// <summary>Creates a sender, all per request setup happens here once.</summary>
	public BulkSender(BulkSenderOptions<TItem, TBody> options)
	{
		if (options is null) throw new ArgumentNullException(nameof(options));
		_transport = options.Transport;
		_action = options.Action;
		_body = options.Body;
		_typeInfo = options.BodyTypeInfo;
		_retry = options.Retry;
		_url = string.IsNullOrWhiteSpace(options.Target)
			? DefaultBulkPathAndQuery
			: options.Target!.Trim('/') + "/" + DefaultBulkPathAndQuery;
	}

	/// <summary>
	/// Serializes <paramref name="items"/> and sends them in a single <c>_bulk</c> request.
	/// Serialization runs synchronously, so a failing action or body throws from this call.
	/// </summary>
	public Task<BulkResponse> SendAsync(ReadOnlySpan<TItem> items, CancellationToken ct = default)
	{
		var buffer = new BulkRequestBuffer(items.Length);
		try
		{
			var writer = buffer.RentWriter();
			try
			{
				foreach (var item in items)
					WriteItem(buffer, writer, item);
			}
			finally { buffer.ReleaseWriter(writer); }
		}
		catch
		{
			buffer.Dispose();
			throw;
		}
		return SendBufferedAsync(buffer, ct);
	}

	/// <summary>Serializes <paramref name="items"/> and sends them in a single <c>_bulk</c> request.</summary>
	public Task<BulkResponse> SendAsync(IEnumerable<TItem> items, CancellationToken ct = default)
	{
		if (items is TItem[] array) return SendAsync(new ReadOnlySpan<TItem>(array), ct);

		var buffer = new BulkRequestBuffer(items is ICollection<TItem> c ? c.Count : 16);
		try
		{
			var writer = buffer.RentWriter();
			try
			{
				foreach (var item in items)
					WriteItem(buffer, writer, item);
			}
			finally { buffer.ReleaseWriter(writer); }
		}
		catch
		{
			buffer.Dispose();
			throw;
		}
		return SendBufferedAsync(buffer, ct);
	}

	private void WriteItem(BulkRequestBuffer buffer, System.Text.Json.Utf8JsonWriter writer, TItem item)
	{
		var action = _action(item);
		if (action.HasBody)
			BulkNdjsonWriter.Write(buffer.Body, writer, in action, _body(item), _typeInfo);
		else
			BulkNdjsonWriter.WritePrefix(buffer.Body, writer, in action);
		buffer.CompleteOperation();
	}

	private Task<BulkResponse> RequestAsync(BulkRequestBuffer buffer, CancellationToken ct) =>
		_transport.RequestAsync<BulkResponse>(HttpMethod.POST, _url,
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
			PostData.ReadOnlyMemory(buffer.Body.WrittenMemory),
#else
			PostData.Bytes(buffer.Body.WrittenSpan.ToArray()),
#endif
			ct);

	private async Task<BulkResponse> SendBufferedAsync(BulkRequestBuffer buffer, CancellationToken ct)
	{
		int[]? map = null;
		BulkResponseItem[]? merged = null;
		var total = buffer.Count;
		try
		{
			var response = await RequestAsync(buffer, ct).ConfigureAwait(false);
			for (var attempt = 0; attempt < _retry.MaxRetries && total > 0; attempt++)
			{
				var current = buffer.Count;
				var details = response.ApiCallDetails;

				int[]? keep = null;
				var keepCount = 0;
				if (!details.HasSuccessfulStatusCode)
				{
					if (!(_retry.RetryAllOnHttp429 && details.HttpStatusCode == 429)) break;
				}
				else
				{
					if (response.Errors == false) break;
					if (response.Items is null || response.Items.Count != current) break;

					keep = ArrayPool<int>.Shared.Rent(current);
					try
					{
						var i = 0;
						if (response.Items is IReadOnlyList<BulkResponseItem> list)
							for (; i < list.Count; i++)
							{
								if (_retry.IsRetryable(list[i])) keep[keepCount++] = i;
							}
						else
							foreach (var item in response.Items)
							{
								if (_retry.IsRetryable(item)) keep[keepCount++] = i;
								i++;
							}

						if (keepCount == 0)
						{
							ArrayPool<int>.Shared.Return(keep);
							keep = null;
							break;
						}
					}
					catch
					{
						ArrayPool<int>.Shared.Return(keep!);
						throw;
					}
				}

				// Record what we know so far for every position of this attempt, retried positions get overwritten by the next one.
				if (details.HasSuccessfulStatusCode)
				{
					merged ??= new BulkResponseItem[total];
					var i = 0;
					foreach (var item in response.Items)
					{
						merged[map is null ? i : map[i]] = item;
						i++;
					}
				}

				if (keep is not null)
				{
					try
					{
						if (map is null)
						{
							map = ArrayPool<int>.Shared.Rent(total);
							for (var i = 0; i < total; i++) map[i] = i;
						}
						// keep (positions in the current attempt) maps back to original positions, in place as keep[j] >= j.
						for (var j = 0; j < keepCount; j++)
							map[j] = map[keep[j]];
						buffer.Compact(keep.AsSpan(0, keepCount));
					}
					finally { ArrayPool<int>.Shared.Return(keep!); }
				}

				await Task.Delay(_retry.Backoff(attempt), ct).ConfigureAwait(false);
				response = await RequestAsync(buffer, ct).ConfigureAwait(false);
			}

			if (merged is null) return response;

			// Merge the final attempt into the positional result.
			if (response.ApiCallDetails.HasSuccessfulStatusCode && response.Items is { } last)
			{
				var i = 0;
				foreach (var item in last)
				{
					if (i < buffer.Count) merged[map is null ? i : map[i]] = item;
					i++;
				}
			}
			response.Items = merged;
			return response;
		}
		finally
		{
			if (map is not null) ArrayPool<int>.Shared.Return(map);
			buffer.Dispose();
		}
	}
}

/// <summary>Factory helpers for the common <see cref="BulkSender{TItem,TBody}"/> shape where the item is the document.</summary>
public static class BulkSender
{
	/// <summary>Creates a sender where each item is both the source of the action and the serialized document.</summary>
	public static BulkSender<T, T> Create<T>(
		ITransport transport,
		JsonTypeInfo<T> typeInfo,
		Func<T, BulkAction> action,
		string? target = null,
		BulkRetryPolicy? retry = null) =>
		new(new BulkSenderOptions<T, T>
		{
			Transport = transport,
			BodyTypeInfo = typeInfo,
			Action = action,
			Body = static item => item,
			Target = target,
			Retry = retry ?? BulkRetryPolicy.None
		});
}
