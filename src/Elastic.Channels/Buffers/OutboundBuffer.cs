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

	/// <inheritdoc cref="IWriteTrackingBuffer.EstimatedBytes"/>
	/// <remarks>Always 0 here; the Elasticsearch channel populates this value in the response callback
	/// once the sub-batch serialization is complete.</remarks>
	public long EstimatedBytes => 0;

	private TEvent[] ArrayItems { get; } = buffer.Reset();

	public ArraySegment<TEvent> GetArraySegment() => new(ArrayItems, 0, Count);

	public void Dispose()
	{
		// The pool is shared and static, so a returned array that still references the exported events keeps them
		// (and their payloads) alive until the pool happens to reuse or trim it.
		// Only the populated part is cleared, so the cost stays proportional to the batch.
		// There is no IsReferenceOrContainsReferences guard: channels constrain TEvent to a class, so the array always holds references.
		Array.Clear(ArrayItems, 0, Count);
		ArrayPool<TEvent>.Shared.Return(ArrayItems);
	}
}
