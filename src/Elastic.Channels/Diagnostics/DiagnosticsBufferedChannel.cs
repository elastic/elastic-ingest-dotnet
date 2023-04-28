// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Channels.Diagnostics;

/// <summary>
/// A NOOP implementation of <see cref="IBufferedChannel{TEvent}"/> that:
/// <para> -tracks the number of times <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.Export"/> is invoked under <see cref="NoopBufferedChannel.ExportedBuffers"/> </para>
/// <para> -observes the maximum concurrent calls to <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.Export"/> under <see cref="NoopBufferedChannel.ObservedConcurrency"/> </para>
/// </summary>
public class DiagnosticsBufferedChannel : NoopBufferedChannel
{
	/// <inheritdoc cref="DiagnosticsBufferedChannel"/>
	public DiagnosticsBufferedChannel(BufferOptions options, bool observeConcurrency = false, string? name = null)
		: base(options, new [] { new ChannelDiagnosticsListener<NoopEvent, NoopResponse>(name ?? nameof(DiagnosticsBufferedChannel)) }, observeConcurrency)
	{
	}

}
