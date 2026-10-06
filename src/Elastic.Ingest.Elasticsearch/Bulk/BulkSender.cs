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
public sealed partial class BulkSender<TItem, TBody>
{
	private readonly ITransport _transport;
	private readonly Func<TItem, BulkAction> _action;
	private readonly Func<TItem, TBody> _body;
	private readonly JsonTypeInfo<TBody> _typeInfo;
	private readonly BulkRetryPolicy _retry;
	private readonly string _url;
	private readonly IRequestConfiguration? _requestConfiguration;
	private readonly ArrayPool<byte> _bytePool;
	private readonly ArrayPool<int> _intPool;
	private readonly ArrayPool<TItem> _itemPool;

	/// <summary>Creates a sender, all per request setup happens here once.</summary>
	public BulkSender(BulkSenderOptions<TItem, TBody> options)
		: this(options, ArrayPool<byte>.Shared, ArrayPool<int>.Shared, ArrayPool<TItem>.Shared) { }

	// the pools are injectable so tests can prove every rented array is returned
	internal BulkSender(BulkSenderOptions<TItem, TBody> options, ArrayPool<byte> bytePool, ArrayPool<int> intPool, ArrayPool<TItem> itemPool)
	{
		_bytePool = bytePool;
		_intPool = intPool;
		_itemPool = itemPool;
		if (options is null) throw new ArgumentNullException(nameof(options));
		_transport = options.Transport;
		_action = options.Action;
		_body = options.Body;
		_typeInfo = options.ApplyLibrarySerializerDefaults ? WithLibraryDefaults(options.BodyTypeInfo) : options.BodyTypeInfo;
		_retry = options.Retry;
		_url = BuildUrl(options.Target, options.Refresh, options.ItemIdentity);

		if (options.RequestTimeout is { } timeout)
		{
			if (timeout <= TimeSpan.Zero && timeout != System.Threading.Timeout.InfiniteTimeSpan)
				throw new ArgumentOutOfRangeException(nameof(options), timeout, "RequestTimeout must be positive or Timeout.InfiniteTimeSpan.");
			_requestConfiguration = new RequestConfiguration { RequestTimeout = timeout };
		}
	}

	// Done once per sender: the target prefix and the refresh parameter are static for its lifetime.
	// The built in filter_path stays untouched, there is no free form query string that could collide with it.
	private static string BuildUrl(string? target, BulkRefresh? refresh, Track track)
	{
		const string bulkPrefix = "_bulk?";
		var query = WithItemIdentity(DefaultBulkPathAndQuery, track).Substring(bulkPrefix.Length);
		if (refresh is { } r)
			query = "refresh=" + r switch
			{
				BulkRefresh.False => "false",
				BulkRefresh.True => "true",
				BulkRefresh.WaitFor => "wait_for",
				_ => throw new ArgumentOutOfRangeException(nameof(refresh), r, "Unknown BulkRefresh value.")
			} + "&" + query;

		var path = bulkPrefix + query;
		return string.IsNullOrWhiteSpace(target) ? path : target!.Trim('/') + "/" + path;
	}

	// Copies the options of the supplied type info (keeping its resolver and converters) with the library default applied.
	// An options level DefaultIgnoreCondition wins over the one a source generated context was declared with, as for channels.
	private static JsonTypeInfo<TBody> WithLibraryDefaults(JsonTypeInfo<TBody> typeInfo)
	{
		var options = typeInfo.Options;
		if (options.DefaultIgnoreCondition == System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault) return typeInfo;
		var copy = new System.Text.Json.JsonSerializerOptions(options)
		{
			DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
		};
		return (JsonTypeInfo<TBody>)copy.GetTypeInfo(typeof(TBody));
	}

	/// <summary>
	/// Serializes <paramref name="items"/> and sends them in a single <c>_bulk</c> request.
	/// Serialization runs synchronously, so a failing action or body throws from this call.
	/// </summary>
	public Task<BulkResponse> SendAsync(ReadOnlySpan<TItem> items, CancellationToken ct = default) =>
		SendCoreAsync(items, _retry, ct);

	private Task<BulkResponse> SendCoreAsync(ReadOnlySpan<TItem> items, BulkRetryPolicy retry, CancellationToken ct)
	{
		var buffer = new BulkRequestBuffer(items.Length, _bytePool, _intPool);
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
		return SendBufferedAsync(buffer, retry, ct);
	}

	/// <summary>Serializes <paramref name="items"/> and sends them in a single <c>_bulk</c> request.</summary>
	public Task<BulkResponse> SendAsync(IEnumerable<TItem> items, CancellationToken ct = default)
	{
		if (items is TItem[] array) return SendAsync(new ReadOnlySpan<TItem>(array), ct);

		var buffer = new BulkRequestBuffer(items is ICollection<TItem> c ? c.Count : 16, _bytePool, _intPool);
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
		return SendBufferedAsync(buffer, _retry, ct);
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

	private Task<BulkResponse> RequestAsync(BulkRequestBuffer buffer, CancellationToken ct)
	{
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
		var body = PostData.ReadOnlyMemory(buffer.Body.WrittenMemory);
#else
		var body = PostData.Bytes(buffer.Body.WrittenSpan.ToArray());
#endif
		// without a configured timeout the request is exactly what it was before
		return _requestConfiguration is { } configuration
			? _transport.RequestAsync<BulkResponse>(HttpMethod.POST, _url, body, configuration, ct)
			: _transport.RequestAsync<BulkResponse>(HttpMethod.POST, _url, body, ct);
	}

	private async Task<BulkResponse> SendBufferedAsync(BulkRequestBuffer buffer, BulkRetryPolicy retry, CancellationToken ct)
	{
		int[]? map = null;
		BulkResponseItem[]? merged = null;
		var total = buffer.Count;
		try
		{
			var response = await RequestAsync(buffer, ct).ConfigureAwait(false);
			for (var attempt = 0; attempt < retry.MaxRetries && total > 0; attempt++)
			{
				var current = buffer.Count;
				var details = response.ApiCallDetails;

				int[]? keep = null;
				var keepCount = 0;
				if (!details.HasSuccessfulStatusCode)
				{
					if (!(retry.RetryAllOnHttp429 && details.HttpStatusCode == 429)) break;
				}
				else
				{
					if (response.Errors == false) break;
					if (response.Items is null || response.Items.Count != current) break;

					keep = _intPool.Rent(current);
					try
					{
						var i = 0;
						if (response.Items is IReadOnlyList<BulkResponseItem> list)
							for (; i < list.Count; i++)
							{
								if (retry.IsRetryable(list[i])) keep[keepCount++] = i;
							}
						else
							foreach (var item in response.Items)
							{
								if (retry.IsRetryable(item)) keep[keepCount++] = i;
								i++;
							}

						if (keepCount == 0)
						{
							_intPool.Return(keep);
							keep = null;
							break;
						}
					}
					catch
					{
						_intPool.Return(keep!);
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
							map = _intPool.Rent(total);
							for (var i = 0; i < total; i++) map[i] = i;
						}
						// keep (positions in the current attempt) maps back to original positions, in place as keep[j] >= j.
						for (var j = 0; j < keepCount; j++)
							map[j] = map[keep[j]];
						buffer.Compact(keep.AsSpan(0, keepCount));
					}
					finally { _intPool.Return(keep!); }
				}

				await Task.Delay(retry.Backoff(attempt), ct).ConfigureAwait(false);
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
			if (map is not null) _intPool.Return(map);
			buffer.Dispose();
		}
	}
}

/// <summary>Factory helpers for the common <see cref="BulkSender{TItem,TBody}"/> shape where the item is the document.</summary>
public static partial class BulkSender
{
	/// <summary>Creates a sender where each item is both the source of the action and the serialized document.</summary>
	public static BulkSender<T, T> Create<T>(
		ITransport transport,
		JsonTypeInfo<T> typeInfo,
		Func<T, BulkAction> action,
		string? target = null,
		BulkRetryPolicy? retry = null,
		BulkRefresh? refresh = null,
		TimeSpan? requestTimeout = null,
		Track itemIdentity = Track.None) =>
		new(new BulkSenderOptions<T, T>
		{
			Transport = transport,
			BodyTypeInfo = typeInfo,
			Action = action,
			Body = static item => item,
			Target = target,
			Retry = retry ?? BulkRetryPolicy.None,
			Refresh = refresh,
			RequestTimeout = requestTimeout
		}.ReturnItemIdentity(itemIdentity));
}
