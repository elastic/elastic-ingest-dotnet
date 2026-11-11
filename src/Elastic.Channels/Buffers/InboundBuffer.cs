// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Buffers;
using System.Threading;

namespace Elastic.Channels.Buffers;

/// <summary>
/// <see cref="InboundBuffer{TEvent}"/> collects the items read from the inbound channel until sufficient items have been added to it
/// or <see cref="DurationSinceFirstWaitToRead"/> exceeds the buffer's maximum lifespan.
/// </summary>
internal class InboundBuffer<TEvent>(int maxBufferSize, TimeSpan forceFlushAfter, TimeProvider timeProvider) : IWriteTrackingBuffer, IDisposable
{
	private TEvent[] Buffer { get; set; } = ArrayPool<TEvent>.Shared.Rent(maxBufferSize);

	/// <summary>The point in time that the first event is read from the channel and added to the buffer,
	/// from the first read or after the buffer is reset.</summary>
	// Timestamps of the time provider's monotonic clock, not wall clock times: a clock that is set back (an NTP step, say) does not age a buffer.
	private long? TimestampOfFirstWrite { get; set; }

	private long? TimestampOfFirstWaitToRead { get; set; }

	private int _count;

	public int Count => _count;

	/// <inheritdoc cref="IWriteTrackingBuffer.EstimatedBytes"/>
	/// <remarks>Always 0 on the inbound buffer; populated at export time by the channel implementation.</remarks>
	public long EstimatedBytes => 0;

	public TimeSpan? DurationSinceFirstWrite => TimestampOfFirstWrite is { } start ? timeProvider.GetElapsedTime(start) : null;
	public TimeSpan? DurationSinceFirstWaitToRead => TimestampOfFirstWaitToRead is { } start ? timeProvider.GetElapsedTime(start) : null;

	public bool NoThresholdsHit => Count == 0
		|| (Count < maxBufferSize && DurationSinceFirstWaitToRead < forceFlushAfter);

	public bool ThresholdsHit => !NoThresholdsHit;

	// not thread safe, buffer is guarded by a single consumer on the inbound channel
	public void Add(TEvent item)
	{
		TimestampOfFirstWrite ??= timeProvider.GetTimestamp();
		Buffer[_count] = item;
		Interlocked.Increment(ref _count);
	}

	public TEvent[] Reset()
	{
		var bufferRef = Buffer;
		_count = 0;
		TimestampOfFirstWrite = null;
		TimestampOfFirstWaitToRead = null;
		Buffer = ArrayPool<TEvent>.Shared.Rent(maxBufferSize);
		return bufferRef;
	}

	/// <summary>Starts the clock of the next buffer: it counts from when the loop first waited for data after the last flush.</summary>
	public void MarkWaiting() => TimestampOfFirstWaitToRead ??= timeProvider.GetTimestamp();

	// the longest due time a Timer accepts is just under 49.7 days
	private static readonly TimeSpan LongestTimer = TimeSpan.FromMilliseconds(uint.MaxValue - 2);

	/// <summary>How long until a buffer that is not full has to be flushed, never more than a timer can wait.</summary>
	public TimeSpan TimeUntilFlush
	{
		get
		{
			var waited = DurationSinceFirstWaitToRead;
			var left = waited is null ? forceFlushAfter : waited.Value >= forceFlushAfter ? TimeSpan.Zero : forceFlushAfter - waited.Value;
			return left > LongestTimer ? LongestTimer : left;
		}
	}

	public void Dispose() {}
}
