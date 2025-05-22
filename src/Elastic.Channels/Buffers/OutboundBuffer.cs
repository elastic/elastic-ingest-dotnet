// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Buffers;

namespace Elastic.Channels.Buffers;

/// <summary>
/// The buffer to be exported over <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.ExportAsync"/>
/// </summary>
/// <remarks>Due to change as we move this over to use ArrayPool</remarks>
public interface IOutboundBuffer<TEvent> : IWriteTrackingBuffer, IDisposable
{
	/// <summary>
	///
	/// </summary>
	/// <returns></returns>
	ArraySegment<TEvent> GetArraySegment();
}

internal class OutboundBuffer<TEvent>(InboundBuffer<TEvent> buffer) : IOutboundBuffer<TEvent>
{
	public int Count { get; } = buffer.Count;

	public TimeSpan? DurationSinceFirstWrite { get; } = buffer.DurationSinceFirstWrite;

	private TEvent[] ArrayItems { get; } = buffer.Reset();

	public ArraySegment<TEvent> GetArraySegment() => new(ArrayItems, 0, Count);

	public void Dispose() => ArrayPool<TEvent>.Shared.Return(ArrayItems);
}
