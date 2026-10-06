// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Buffers;

namespace Elastic.Channels.Buffers;

/// <summary>A batch pulled from a sequence into an <see cref="ArrayPool{T}"/> array, exported like any other outbound buffer.</summary>
internal sealed class PullBuffer<TEvent>(TEvent[] items, int count, TimeSpan? duration) : IOutboundBuffer<TEvent>
{
	public int Count { get; } = count;

	public TimeSpan? DurationSinceFirstWrite { get; } = duration;

	public long EstimatedBytes => 0;

	public ArraySegment<TEvent> GetArraySegment() => new(items, 0, Count);

	public void Dispose()
	{
		// do not keep the exported events alive through the pooled array
		Array.Clear(items, 0, Count);
		ArrayPool<TEvent>.Shared.Return(items);
	}
}
