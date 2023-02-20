// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Elastic.Channels.Diagnostics;

/// <summary>
/// A NOOP implementation of <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/> that:
/// <para> -tracks the number of times <see cref="Export"/> is invoked under <see cref="ExportedBuffers"/> </para>
/// <para> -observes the maximum concurrent calls to <see cref="Export"/> under <see cref="ObservedConcurrency"/> </para>
/// </summary>
public class NoopBufferedChannel
	: BufferedChannelBase<NoopBufferedChannel.NoopChannelOptions, NoopBufferedChannel.NoopEvent, NoopBufferedChannel.NoopResponse>
{
	/// <summary> Empty event for use with <see cref="NoopBufferedChannel"/> </summary>
	public class NoopEvent { }

	/// <summary> Empty response for use with <see cref="NoopBufferedChannel"/> </summary>
	public class NoopResponse { }

	/// <summary> Provides options how the <see cref="NoopBufferedChannel"/> should behave </summary>
	public class NoopChannelOptions : ChannelOptionsBase<NoopEvent, NoopResponse>
	{
		/// <summary> If set (defaults:false) will track the max observed concurrency to <see cref="NoopBufferedChannel.Export"/></summary>
		public bool TrackConcurrency { get; set; }
	}

	/// <inheritdoc cref="NoopBufferedChannel"/>
	public NoopBufferedChannel(BufferOptions options, bool observeConcurrency = false) : base(new NoopChannelOptions
	{
		BufferOptions = options, TrackConcurrency = observeConcurrency
	}) { }

	/// <summary> Returns the number of times <see cref="Export"/> was called</summary>
	public long ExportedBuffers => _exportedBuffers;

	private long _exportedBuffers;

	/// <summary> The maximum observed concurrency to calls to <see cref="Export"/>, requires <see cref="NoopChannelOptions.TrackConcurrency"/> to be set</summary>
	public int ObservedConcurrency { get; private set; }

	private int _currentMax;

	/// <inheritdoc cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.Export"/>
	protected override async Task<NoopResponse> Export(IReadOnlyCollection<NoopEvent> buffer, CancellationToken ctx = default)
	{
		Interlocked.Increment(ref _exportedBuffers);
		if (!Options.TrackConcurrency) return new NoopResponse();

		var max = Interlocked.Increment(ref _currentMax);
		await Task.Delay(TimeSpan.FromMilliseconds(1), ctx).ConfigureAwait(false);
		Interlocked.Decrement(ref _currentMax);
		if (max > ObservedConcurrency) ObservedConcurrency = max;
		return new NoopResponse();
	}
}
