// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Elastic.Channels;

/// <summary>
/// A NOOP implementation of <see cref="BufferedChannelBase{TEvent,TResponse}"/> that:
/// <para> -tracks the number of times <see cref="Send"/> is invoked under <see cref="SentBuffersCount"/> </para>
/// <para> -observes the maximum concurrent calls to <see cref="Send"/> under <see cref="ObservedConcurrency"/> </para>
/// </summary>
public class NoopBufferedChannel
	: BufferedChannelBase<NoopBufferedChannel.NoopChannelOptions, NoopBufferedChannel.NoopEvent, NoopBufferedChannel.NoopResponse>
{
	public class NoopEvent { }
	public class NoopResponse { }
	public class NoopChannelOptions : ChannelOptionsBase<NoopEvent, NoopResponse> { }

	public NoopBufferedChannel(BufferOptions options) : base(new NoopChannelOptions { BufferOptions = options }) { }

	private long _sentBuffersCount;
	public long SentBuffersCount => _sentBuffersCount;

	private int _currentMax;
	public int ObservedConcurrency { get; private set; }

	protected override async Task<NoopResponse> Send(IReadOnlyCollection<NoopEvent> buffer)
	{
		Interlocked.Increment(ref _sentBuffersCount);
		var max = Interlocked.Increment(ref _currentMax);
		await Task.Delay(TimeSpan.FromMilliseconds(1)).ConfigureAwait(false);
		Interlocked.Decrement(ref _currentMax);
		if (max > ObservedConcurrency) ObservedConcurrency = max;
		return new NoopResponse();
	}
}