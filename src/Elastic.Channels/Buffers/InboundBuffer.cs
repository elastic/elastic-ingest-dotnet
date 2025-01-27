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
internal class InboundBuffer<TEvent> : IWriteTrackingBuffer, IDisposable
{
	private readonly int _maxBufferSize;
	private readonly TimeSpan _forceFlushAfter;

	private CancellationTokenSource _breaker = new();

	private TEvent[] Buffer { get; set; }

	/// <summary>The time that the first event is read from the channel and added to the buffer, from first read or after the buffer is reset.</summary>
	private DateTimeOffset? TimeOfFirstWrite { get; set; }
	private DateTimeOffset? TimeOfFirstWaitToRead { get; set; }

	private int _count = 0;
	public int Count => _count;
	public TimeSpan? DurationSinceFirstWrite => DateTimeOffset.UtcNow - TimeOfFirstWrite;
	public TimeSpan? DurationSinceFirstWaitToRead => DateTimeOffset.UtcNow - TimeOfFirstWaitToRead;

	public bool NoThresholdsHit => Count == 0
		|| (Count < _maxBufferSize && DurationSinceFirstWaitToRead <= _forceFlushAfter);

	public bool ThresholdsHit => !NoThresholdsHit;

	public InboundBuffer(int maxBufferSize, TimeSpan forceFlushAfter)
	{
		_maxBufferSize = maxBufferSize;
		_forceFlushAfter = forceFlushAfter;
		Buffer = ArrayPool<TEvent>.Shared.Rent(maxBufferSize);
		TimeOfFirstWrite = null;
	}

	// not thread safe, buffer is guarded by a single consumer on the inbound channel
	public void Add(TEvent item)
	{
		TimeOfFirstWrite ??= DateTimeOffset.UtcNow;
		Buffer[_count] = item;
		Interlocked.Increment(ref _count);
	}

	public TEvent[] Reset()
	{
		_count = 0;
		TimeOfFirstWrite = null;
		TimeOfFirstWaitToRead = null;
		var bufferRef = Buffer;
		Buffer = ArrayPool<TEvent>.Shared.Rent(_maxBufferSize);
		return bufferRef;
	}

	private TimeSpan Wait
	{
		get
		{
			if (!DurationSinceFirstWaitToRead.HasValue) return _forceFlushAfter;

			var d = DurationSinceFirstWaitToRead.Value;
			return d < _forceFlushAfter ? _forceFlushAfter - d : _forceFlushAfter;
		}
	}

	/// <summary>
	/// Call <see cref="ChannelReader{T}.WaitToReadAsync"/> with a timeout to force a flush to happen every
	/// <see cref="_forceFlushAfter"/>. This tries to avoid allocation too many <see cref="CancellationTokenSource"/>'s
	/// needlessly and reuses them if possible.
	/// </summary>
	public async Task<WaitToReadResult> WaitToReadAsync(ChannelReader<TEvent?> reader)
	{
		TimeOfFirstWaitToRead ??= DateTimeOffset.UtcNow;
		if (_breaker.IsCancellationRequested)
		{
			_breaker.Dispose();
			_breaker = new CancellationTokenSource();
		}

		try
		{
			// https://github.com/dotnet/runtime/issues/761
			// cancellation tokens may not be unrooted properly by design if cancellation occurs.
			// by checking explicitly which task ends up being completed we can uncover when

			// We accept the possibility of several pending tasks blocking on WaitToReadAsync()
			// These will all unblock and free up when a new message gets pushed.
			// To aid with cleaning these tasks up we write `default` to the channel when this task returns TimeOut

			_breaker.CancelAfter(Wait);
			var readTask = reader.WaitToReadAsync().AsTask();
			var delayTask = Task.Delay(Timeout.Infinite, _breaker.Token);
			var completedTask = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);

			if (completedTask == delayTask)
				throw new OperationCanceledException(_breaker.Token);

			_breaker.CancelAfter(-1);
			return await readTask.ConfigureAwait(false) ? WaitToReadResult.Read : WaitToReadResult.Completed;
		}
		catch (Exception) when (_breaker.IsCancellationRequested)
		{
			_breaker.Dispose();
			_breaker = new CancellationTokenSource();
			return WaitToReadResult.Timeout;
		}
		catch (Exception)
		{
			_breaker.CancelAfter(Wait);
			return WaitToReadResult.Read;
		}
	}

	public void Dispose() => _breaker.Dispose();
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
