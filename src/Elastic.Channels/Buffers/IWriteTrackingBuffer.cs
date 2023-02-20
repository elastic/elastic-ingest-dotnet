// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;

namespace Elastic.Channels.Buffers;

/// <summary>
/// Represents a buffer that tracks the <see cref="DurationSinceFirstWrite"/> and it's current <see cref="Count"/>
/// <para>Used by both <see cref="InboundBuffer{TEvent}"/> and <see cref="OutboundBuffer{TEvent}"/></para>
/// </summary>
public interface IWriteTrackingBuffer
{
	/// <summary> The current size of the buffer  </summary>
	int Count { get; }
	/// <summary>
	/// The duration since the first write
	/// </summary>
	TimeSpan? DurationSinceFirstWrite { get; }
}
