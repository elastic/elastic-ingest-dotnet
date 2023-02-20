// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;

namespace Elastic.Channels.Buffers;

/// <summary>
/// The buffer to be exported over <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.Export"/>
/// </summary>
/// <remarks>Due to change as we move this over to use ArrayPool</remarks>
public interface IOutboundBuffer<out TEvent> : IWriteTrackingBuffer
{
	/// <inheritdoc cref="IOutboundBuffer{TEvent}"/>
	public IReadOnlyCollection<TEvent> Items { get; }
}

internal class OutboundBuffer<TEvent> : IOutboundBuffer<TEvent>
{
	public IReadOnlyCollection<TEvent> Items { get; }

	public OutboundBuffer(InboundBuffer<TEvent> buffer)
	{
		Count = buffer.Count;
		DurationSinceFirstWrite = buffer.DurationSinceFirstWrite;
		// create a shallow copied collection to hand to consumers.
		Items = buffer.Copy();
	}

	public int Count { get; }
	public TimeSpan? DurationSinceFirstWrite { get; }
}
