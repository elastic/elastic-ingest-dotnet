// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Elastic.Channels;

/// <summary>
/// <see cref="InboundBuffer{TEvent}"/> is a buffer that will block <see cref="WaitToReadAsync"/> until
/// sufficient items have been added to it or <see cref="DurationSinceFirstWrite"/> exceeds the buffer's maximum lifespan.
/// </summary>
internal class InboundBuffer<TEvent> : IWriteTrackingBuffer, IDisposable
{
	private readonly int _maxBufferSize;
	private readonly TimeSpan _forceFlushAfter;

	private CancellationTokenSource _breaker = new();

	public List<TEvent> Buffer { get; }

	/// <summary>The time that the first event is read from the channel and added to the buffer, from first read or after the buffer is reset.</summary>
	private DateTimeOffset? TimeOfFirstWrite { get; set; }

	public int Count => Buffer.Count;
	public TimeSpan? DurationSinceFirstWrite => DateTimeOffset.UtcNow - TimeOfFirstWrite;
	public bool NoThresholdsHit => Count == 0
		|| (Count < _maxBufferSize && DurationSinceFirstWrite <= _forceFlushAfter);

	public InboundBuffer(int maxBufferSize, TimeSpan forceFlushAfter)
	{
		_maxBufferSize = maxBufferSize;
		_forceFlushAfter = forceFlushAfter;
		Buffer = new List<TEvent>(maxBufferSize);
		TimeOfFirstWrite = null;
	}

	public void Add(TEvent item)
	{
		TimeOfFirstWrite ??= DateTimeOffset.UtcNow;
		Buffer.Add(item);
	}

	public void Reset()
	{
		Buffer.Clear();
		TimeOfFirstWrite = null;
	}

	public TEvent[] Copy()
	{
		var outgoingBuffer = new TEvent[Buffer.Count];
		Buffer.CopyTo(outgoingBuffer);
		return outgoingBuffer;
	}

	private TimeSpan Wait
	{
		get
		{
			if (!DurationSinceFirstWrite.HasValue) return _forceFlushAfter;

			var d = DurationSinceFirstWrite.Value;
			return d < _forceFlushAfter ? _forceFlushAfter - d : _forceFlushAfter;
		}
	}

	/// <summary>
	/// Call <see cref="ChannelReader{T}.WaitToReadAsync"/> with a timeout to force a flush to happen every
	/// <see cref="_forceFlushAfter"/>. This tries to avoid allocation too many <see cref="CancellationTokenSource"/>'s
	/// needlessly and reuses them if possible.
	/// </summary>
	public async Task<bool> WaitToReadAsync(ChannelReader<TEvent> reader)
	{
		if (_breaker.IsCancellationRequested)
		{
			_breaker.Dispose();
			_breaker = new CancellationTokenSource();
		}

		try
		{
			var w = Count == 0 ? _forceFlushAfter : Wait;
			_breaker.CancelAfter(w);
			var _ = await reader.WaitToReadAsync(_breaker.Token).ConfigureAwait(false);
			_breaker.CancelAfter(-1);
			return true;
		}
		catch (Exception) when (_breaker.IsCancellationRequested)
		{
			_breaker.Dispose();
			_breaker = new CancellationTokenSource();
			return true;
		}
		catch (Exception)
		{
			_breaker.CancelAfter(-1);
			return true;
		}
	}

	public void Dispose() => _breaker.Dispose();
}
