// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Bulk;

/// <summary>Options for <c>IngestAllAsync</c> on a <see cref="BulkSender{TItem,TBody}"/>.</summary>
public sealed record IngestAllOptions
{
	/// <summary>The number of items per <c>_bulk</c> request. Defaults to 1,000.</summary>
	public int BatchSize { get; init; } = 1_000;

	/// <summary>The maximum number of requests in flight, <c>1</c> sends batches strictly in order. Defaults to the processor count.</summary>
	public int MaxConcurrency { get; init; } = Environment.ProcessorCount;

	/// <summary>Overrides the retry policy of the sender for this call.</summary>
	public BulkRetryPolicy? Retry { get; init; }
}

/// <summary>An item that was not persisted.</summary>
/// <param name="Position">The zero based position of the item in the source sequence.</param>
/// <param name="Item">The final response for the item.</param>
public readonly record struct BulkIngestFailure(long Position, BulkResponseItem Item);

/// <summary>The outcome of <c>IngestAllAsync</c> on a <see cref="BulkSender{TItem,TBody}"/>.</summary>
public sealed class BulkIngestAllResult : IngestAllResult
{
	internal BulkIngestAllResult(long read, long batches, IReadOnlyList<BulkIngestFailure> failures)
		: base(read, batches, failures.Count) => Failures = failures;

	/// <summary>Every item that was still failing after retries, ordered by position. Empty when everything was persisted.</summary>
	public IReadOnlyList<BulkIngestFailure> Failures { get; }

	/// <summary>The number of items that were persisted.</summary>
	public long Succeeded => Read - Failures.Count;
}

public sealed partial class BulkSender<TItem, TBody>
{
	/// <summary>
	/// Pulls <paramref name="source"/> to exhaustion, sends it in batches and returns once every batch settled.
	/// Retries use the sender's <see cref="BulkRetryPolicy"/> unless <see cref="IngestAllOptions.Retry"/> overrides it.
	/// </summary>
	public async Task<BulkIngestAllResult> IngestAllAsync(IEnumerable<TItem> source, IngestAllOptions? options = null, CancellationToken ct = default)
	{
		var run = new IngestRun(this, options, ct);
		try
		{
			if (source is TItem[] array)
			{
				for (var offset = 0; offset < array.Length; offset += run.BatchSize)
					await run.StartAsync(array, offset, Math.Min(run.BatchSize, array.Length - offset), rented: false).ConfigureAwait(false);
			}
			else
			{
				var batch = _itemPool.Rent(run.BatchSize);
				var count = 0;
				try
				{
					foreach (var item in source)
					{
						ct.ThrowIfCancellationRequested();
						batch[count++] = item;
						if (count < run.BatchSize) continue;
						var full = batch;
						batch = _itemPool.Rent(run.BatchSize);
						await run.StartAsync(full, 0, count, rented: true).ConfigureAwait(false);
						count = 0;
					}
					if (count > 0)
					{
						var last = batch;
						batch = null!;
						await run.StartAsync(last, 0, count, rented: true).ConfigureAwait(false);
					}
				}
				finally
				{
					if (batch is not null) _itemPool.Return(batch, clearArray: true);
				}
			}
			return await run.CompleteAsync().ConfigureAwait(false);
		}
		catch
		{
			await run.AbortAsync().ConfigureAwait(false);
			throw;
		}
	}

	/// <inheritdoc cref="IngestAllAsync(IEnumerable{TItem},IngestAllOptions,CancellationToken)"/>
	public async Task<BulkIngestAllResult> IngestAllAsync(IAsyncEnumerable<TItem> source, IngestAllOptions? options = null, CancellationToken ct = default)
	{
		var run = new IngestRun(this, options, ct);
		var batch = _itemPool.Rent(run.BatchSize);
		var count = 0;
		try
		{
			await foreach (var item in source.WithCancellation(ct).ConfigureAwait(false))
			{
				batch[count++] = item;
				if (count < run.BatchSize) continue;
				var full = batch;
				batch = _itemPool.Rent(run.BatchSize);
				await run.StartAsync(full, 0, count, rented: true).ConfigureAwait(false);
				count = 0;
			}
			if (count > 0)
			{
				var last = batch;
				batch = null!;
				await run.StartAsync(last, 0, count, rented: true).ConfigureAwait(false);
			}
			return await run.CompleteAsync().ConfigureAwait(false);
		}
		catch
		{
			await run.AbortAsync().ConfigureAwait(false);
			throw;
		}
		finally
		{
			if (batch is not null) _itemPool.Return(batch, clearArray: true);
		}
	}

	private sealed class IngestRun
	{
		private readonly BulkSender<TItem, TBody> _sender;
		private readonly BulkRetryPolicy _retry;
		private readonly int _concurrency;
		private readonly CancellationToken _ct;
		private readonly List<Task> _inflight = new();
		private readonly List<BulkIngestFailure> _failures = new();
		private long _read;
		private long _batches;

		public IngestRun(BulkSender<TItem, TBody> sender, IngestAllOptions? options, CancellationToken ct)
		{
			_sender = sender;
			_ct = ct;
			_retry = options?.Retry ?? sender._retry;
			BatchSize = Math.Max(1, options?.BatchSize ?? 1_000);
			_concurrency = Math.Max(1, options?.MaxConcurrency ?? Environment.ProcessorCount);
		}

		public int BatchSize { get; }

		public async Task StartAsync(TItem[] items, int offset, int length, bool rented)
		{
			try
			{
				_ct.ThrowIfCancellationRequested();
				while (_inflight.Count >= _concurrency)
					await ReapOneAsync().ConfigureAwait(false);
			}
			catch
			{
				// never handed over, so it is ours to return
				if (rented) _sender._itemPool.Return(items, clearArray: true);
				throw;
			}

			var position = _read;
			_read += length;
			_batches++;

			// serialization completes synchronously inside SendCore, so a rented array is free to go back right after
			Task<BulkResponse> send;
			try { send = _sender.SendCoreAsync(new ReadOnlySpan<TItem>(items, offset, length), _retry, _ct); }
			finally
			{
				if (rented) _sender._itemPool.Return(items, clearArray: true);
			}

			var tracked = TrackAsync(send, position, length);
			if (_concurrency == 1) await tracked.ConfigureAwait(false);
			else _inflight.Add(tracked);
		}

		private async Task TrackAsync(Task<BulkResponse> send, long position, int length)
		{
#pragma warning disable VSTHRD003 // started by SendCoreAsync in this same call
			var response = await send.ConfigureAwait(false);
#pragma warning restore VSTHRD003
			if (response.ApiCallDetails.HasSuccessfulStatusCode && response.Items is { } items && items.Count == length)
			{
				var i = 0;
				foreach (var item in items)
				{
					if (item.Status is < 200 or > 299) Add(new BulkIngestFailure(position + i, item));
					i++;
				}
				return;
			}

			// the request itself failed, every item of the batch is lost
			var status = response.ApiCallDetails.HttpStatusCode ?? 0;
			for (var i = 0; i < length; i++)
				Add(new BulkIngestFailure(position + i, new BulkResponseItem { Action = "index", Status = status }));
		}

		private void Add(BulkIngestFailure failure)
		{
			lock (_failures) _failures.Add(failure);
		}

		private async Task ReapOneAsync()
		{
			var done = await Task.WhenAny(_inflight).ConfigureAwait(false);
			_inflight.Remove(done);
#pragma warning disable VSTHRD003 // the task is one of ours, it was started by TrackAsync
			await done.ConfigureAwait(false);
#pragma warning restore VSTHRD003
		}

		public async Task<BulkIngestAllResult> CompleteAsync()
		{
			while (_inflight.Count > 0)
				await ReapOneAsync().ConfigureAwait(false);
			lock (_failures)
				return new BulkIngestAllResult(_read, _batches, _failures.OrderBy(f => f.Position).ToArray());
		}

		public async Task AbortAsync()
		{
			try { await Task.WhenAll(_inflight).ConfigureAwait(false); }
			catch
			{
				// the original failure is the one that matters
			}
		}
	}
}

public static partial class BulkSender
{
	/// <summary>
	/// The one-off helper: stores <paramref name="items"/> in <paramref name="target"/> and returns when every batch settled.
	/// Retries 429/502/503/504 per item unless <see cref="IngestAllOptions.Retry"/> says otherwise.
	/// </summary>
	/// <param name="transport">The transport to send over.</param>
	/// <param name="typeInfo">Serialization metadata for <typeparamref name="T"/>, use a source generated context for AOT.</param>
	/// <param name="items">The documents to store.</param>
	/// <param name="target">The index or data stream, optional when <paramref name="action"/> sets an index per item.</param>
	/// <param name="action">Decides the action per item, defaults to <see cref="BulkAction.Index"/> with a server generated id.</param>
	/// <param name="options">Batch size, concurrency and retry.</param>
	/// <param name="ct">Cancels the operation.</param>
	public static Task<BulkIngestAllResult> IngestAllAsync<T>(
		ITransport transport,
		JsonTypeInfo<T> typeInfo,
		IEnumerable<T> items,
		string? target = null,
		Func<T, BulkAction>? action = null,
		IngestAllOptions? options = null,
		CancellationToken ct = default) =>
		Create(transport, typeInfo, action ?? (static _ => BulkAction.Index()), target, options?.Retry ?? BulkRetryPolicy.Default)
			.IngestAllAsync(items, options, ct);

	/// <inheritdoc cref="IngestAllAsync{T}(ITransport,JsonTypeInfo{T},IEnumerable{T},string,Func{T,BulkAction},IngestAllOptions,CancellationToken)"/>
	public static Task<BulkIngestAllResult> IngestAllAsync<T>(
		ITransport transport,
		JsonTypeInfo<T> typeInfo,
		IAsyncEnumerable<T> items,
		string? target = null,
		Func<T, BulkAction>? action = null,
		IngestAllOptions? options = null,
		CancellationToken ct = default) =>
		Create(transport, typeInfo, action ?? (static _ => BulkAction.Index()), target, options?.Retry ?? BulkRetryPolicy.Default)
			.IngestAllAsync(items, options, ct);
}
