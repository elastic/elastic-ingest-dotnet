// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Buffers;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Elastic.Channels.Buffers;

/// <summary>
/// <see cref="InboundBuffer{TEvent}"/> is a buffer that will block <see cref="WaitToReadAsync"/> until
/// sufficient items have been added to it or <see cref="DurationSinceFirstWrite"/> exceeds the buffer's maximum lifespan.
/// </summary>
internal class InboundBuffer<TEvent>(int maxBufferSize, TimeSpan forceFlushAfter, long? maxBufferBytes = null) : IWriteTrackingBuffer, IDisposable
{
	private TEvent[] Buffer { get; set; } = ArrayPool<TEvent>.Shared.Rent(maxBufferSize);

	/// <summary>The point in time that the first event is read from the channel and added to the buffer,
	/// from the first read or after the buffer is reset.</summary>
	private DateTimeOffset? TimeOfFirstWrite { get; set; }

	private DateTimeOffset? TimeOfFirstWaitToRead { get; set; }

	private int _count;
	private long _estimatedBytes;

	public int Count => _count;
	public long EstimatedBytes => _estimatedBytes;

	/// <summary> Average bytes per event in the current batch. Used to preemptively flush before the next item
	/// would tip us over the budget even if its exact size is not yet known. </summary>
	public long AverageItemBytes => _count > 0 ? _estimatedBytes / _count : 0;

	public TimeSpan? DurationSinceFirstWrite => DateTimeOffset.UtcNow - TimeOfFirstWrite;
	public TimeSpan? DurationSinceFirstWaitToRead => DateTimeOffset.UtcNow - TimeOfFirstWaitToRead;

	public bool NoThresholdsHit => Count == 0
		|| (Count < maxBufferSize
			&& DurationSinceFirstWaitToRead <= forceFlushAfter
			&& (!maxBufferBytes.HasValue || _estimatedBytes < maxBufferBytes.Value));

	public bool ThresholdsHit => !NoThresholdsHit;

	/// <summary>
	/// Returns true when adding an event whose serialized size is <paramref name="nextItemBytes"/> would push the
	/// running byte total past the configured byte budget, taking into account the rolling average so the buffer
	/// flushes preemptively when the next item is likely to tip it over (e.g. flush at 900 KB when average is
	/// 100 KB and the budget is 1 MB).
	/// </summary>
	public bool WouldExceedBytes(long nextItemBytes) =>
		maxBufferBytes.HasValue
		&& _count > 0
		&& _estimatedBytes + Math.Max(nextItemBytes, AverageItemBytes) > maxBufferBytes.Value;

	// not thread safe, buffer is guarded by a single consumer on the inbound channel
	public void Add(TEvent item, long itemBytes = 0)
	{
		TimeOfFirstWrite ??= DateTimeOffset.UtcNow;
		Buffer[_count] = item;
		Interlocked.Increment(ref _count);
		_estimatedBytes += itemBytes;
	}

	public TEvent[] Reset()
	{
		var bufferRef = Buffer;
		_count = 0;
		_estimatedBytes = 0;
		TimeOfFirstWrite = null;
		TimeOfFirstWaitToRead = null;
		Buffer = ArrayPool<TEvent>.Shared.Rent(maxBufferSize);
		return bufferRef;
	}

	private TimeSpan Wait
	{
		get
		{
			if (!DurationSinceFirstWaitToRead.HasValue) return forceFlushAfter;

			var d = DurationSinceFirstWaitToRead.Value;
			return d < forceFlushAfter ? forceFlushAfter - d : forceFlushAfter;
		}
	}

	private Task<bool>? _cachedWaitToRead;
	/// Call <see cref="ChannelReader{T}.WaitToReadAsync"/> with a timeout to force a flush to happen every <see cref="Wait"/>.
	public async Task<WaitToReadResult> WaitToReadAsync(ChannelReader<TEvent?> reader, CancellationToken ctx)
	{
		TimeOfFirstWaitToRead ??= DateTimeOffset.UtcNow;

		try
		{
			// https://github.com/dotnet/runtime/issues/761
			// cancellation tokens may not be unrooted properly by design if cancellation occurs.
			// by checking explicitly which task ends up being completed, we can uncover when

			// We accept the possibility of several pending tasks blocking on WaitToReadAsync()
			// These will all unblock and free up when a new message gets pushed.
			// To aid with cleaning these tasks up, we write `default` to the channel when this task returns TimeOut

			var readTask = _cachedWaitToRead ?? reader.WaitToReadAsync(ctx).AsTask();
			_cachedWaitToRead = null;
			var delayTask = Task.Delay(Wait, ctx);
			var completedTask = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);

			if (completedTask == delayTask)
			{
				_cachedWaitToRead = readTask;
				return WaitToReadResult.Timeout;
			}

			return await readTask.ConfigureAwait(false) ? WaitToReadResult.Read : WaitToReadResult.Completed;
		}
		catch (OperationCanceledException)
		{
			return WaitToReadResult.Timeout;
		}
		catch (Exception) when (ctx.IsCancellationRequested)
		{
			return WaitToReadResult.Timeout;
		}
		catch (Exception)
		{
			return WaitToReadResult.Read;
		}
	}

	public void Dispose() {}
}

/// <summary>
/// Signals to consumer the result of a call to <see cref="ChannelReader{T}.WaitToReadAsync"/> that may have been cancelled.
/// </summary>
public enum WaitToReadResult
{
	/// the wait resulted in data
	Read,
	/// the wait can no longer receive any data ever
	Completed,
	///
	Timeout
}
