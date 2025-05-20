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
/// <para> - tracks the number of times <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.ExportAsync"/> is invoked under <see cref="NoopBufferedChannel.ExportedBuffers"/> </para>
/// <para> - observes the maximum concurrent calls to <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.ExportAsync"/> under <see cref="NoopBufferedChannel.ObservedConcurrency"/> </para>
/// <para> - tracks how often the buffer does not match the export size or the export buffers segment does not start at the expected offset </para>
/// </summary>
public class DiagnosticsBufferedChannel : NoopBufferedChannel
{
	/// <inheritdoc cref="DiagnosticsBufferedChannel"/>
	public DiagnosticsBufferedChannel(BufferOptions options, bool observeConcurrency = false, string? name = null)
		: base(options, new [] { new ChannelDiagnosticsListener<NoopEvent, NoopResponse>(name ?? nameof(DiagnosticsBufferedChannel)) }, observeConcurrency)
	{
	}

	/// <inheritdoc cref="DiagnosticsBufferedChannel"/>
	public DiagnosticsBufferedChannel(NoopChannelOptions options, string? name = null)
		: base(options, new [] { new ChannelDiagnosticsListener<NoopEvent, NoopResponse>(name ?? nameof(DiagnosticsBufferedChannel)) })
	{
	}

	private long _bufferMismatches;
	/// <summary> Keeps track of the number of times the buffer size or the buffer offset was off</summary>
	public long BufferMismatches => _bufferMismatches;

	/// <inheritdoc cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.ExportAsync"/>
	protected override Task<NoopResponse> ExportAsync(ArraySegment<NoopEvent> buffer, CancellationToken ctx = default)
	{
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
		var b = buffer;
#else
		IList<NoopEvent> b = buffer;
#endif
		if (BatchExportSize != buffer.Count)
			Interlocked.Increment(ref _bufferMismatches);

		return base.ExportAsync(buffer, ctx);
	}

	/// <summary>
	/// Provides a debug message to give insights to the machinery of <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/>
	/// </summary>
	public override string ToString() => $@"{base.ToString()}
{nameof(BufferMismatches)}: {BufferMismatches:N0}
------------------------------------------";
}
