// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Elastic.Channels.Diagnostics;

/// <summary>
/// A NOOP implementation of <see cref="IBufferedChannel{TEvent}"/> that:
/// <para> - tracks the number of times <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.Export"/> is invoked under <see cref="NoopBufferedChannel.ExportedBuffers"/> </para>
/// <para> - observes the maximum concurrent calls to <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.Export"/> under <see cref="NoopBufferedChannel.ObservedConcurrency"/> </para>
/// <para> - tracks how often the buffer does not match the export size or the export buffers segment does not start at the expected offset </para>
/// </summary>
public class DiagnosticsBufferedChannel : NoopBufferedChannel
{
	/// <inheritdoc cref="DiagnosticsBufferedChannel"/>
	public DiagnosticsBufferedChannel(BufferOptions options, bool observeConcurrency = false, string? name = null)
		: base(options, new [] { new ChannelDiagnosticsListener<NoopEvent, NoopResponse>(name ?? nameof(DiagnosticsBufferedChannel)) }, observeConcurrency)
	{
	}

	private long _bufferMismatches;
	/// <summary> Keeps track of the number of times the buffer size or the buffer offset was off</summary>
	public long BufferMismatches => _bufferMismatches;

	/// <inheritdoc cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.Export"/>
	protected override Task<NoopResponse> Export(ArraySegment<NoopEvent> buffer, CancellationToken ctx = default)
	{
		#if NETSTANDARD2_1
		var b = buffer;
		#else
		IList<NoopEvent> b = buffer;
		#endif
		if (Options.BufferOptions.OutboundBufferMaxSize != buffer.Count)
		{
			Interlocked.Increment(ref _bufferMismatches);
		}
		else if (b.Count > 0 && b[0].Id.HasValue)
		{
			if (b[0].Id % Options.BufferOptions.OutboundBufferMaxSize != 0)
				Interlocked.Increment(ref _bufferMismatches);
		}

		return base.Export(buffer, ctx);
	}

	/// <summary>
	/// Provides a debug message to give insights to the machinery of <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/>
	/// </summary>
	public override string ToString() => $@"{base.ToString()}
{nameof(BufferMismatches)}: {BufferMismatches:N0}
------------------------------------------";
}
