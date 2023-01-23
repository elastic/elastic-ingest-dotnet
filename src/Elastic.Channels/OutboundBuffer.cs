// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Elastic.Channels;

public interface IConsumedBuffer<out TEvent> : IWriteTrackingBuffer
{
	public IReadOnlyCollection<TEvent> Items { get; }
}

internal class OutboundBuffer<TEvent> : IConsumedBuffer<TEvent>
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
