// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using static Elastic.Channels.Diagnostics.DiagnosticsBufferedChannel;

namespace Elastic.Channels.Diagnostics;

/// <summary>
/// A NOOP implementation of <see cref="BufferedChannelBase{TEvent,TResponse}"/> that:
/// <para> -tracks the number of times <see cref="Send"/> is invoked under <see cref="SentBuffersCount"/> </para>
/// <para> -observes the maximum concurrent calls to <see cref="Send"/> under <see cref="ObservedConcurrency"/> </para>
/// </summary>
public class DiagnosticsBufferedChannel : NoopBufferedChannel
{
	private readonly string? _name;

	public DiagnosticsBufferedChannel(BufferOptions options, bool observeConcurrency = false, string? name = null)
		: base(options, observeConcurrency)
	{
		_name = name;
		Listener = new ChannelListener<NoopEvent, NoopResponse>(_name).Register(Options);
	}

	public ChannelListener<NoopEvent, NoopResponse> Listener { get; }

	public override string ToString() => $@"{Listener}
Send Invocations: {SentBuffersCount:N0}
Observed Concurrency: {ObservedConcurrency:N0}
";
}
