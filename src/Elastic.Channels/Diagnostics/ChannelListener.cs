// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;

namespace Elastic.Channels.Diagnostics;

public class ChannelListener<TEvent, TResponse>
{
	private readonly string? _name;
	private int _exportedBuffers;

	public Exception? ObservedException { get; private set; }

	public virtual bool PublishSuccess => ObservedException == null && _exportedBuffers > 0 && _maxRetriesExceeded == 0 && _items > 0;

	public ChannelListener(string? name = null) => _name = name;

	private long _responses;
	private long _retries;
	private long _items;
	private long _maxRetriesExceeded;
	private long _outboundPublishes;
	private long _outboundPublishFailures;
	private long _inboundPublishes;
	private long _inboundPublishFailures;
	private bool _outboundChannelStarted;
	private bool _inboundChannelStarted;
	private bool _outboundChannelExited;

	// ReSharper disable once MemberCanBeProtected.Global
	public ChannelListener<TEvent, TResponse> Register(ChannelOptionsBase<TEvent, TResponse> options)
	{
		options.BufferOptions.ExportBufferCallback = () => Interlocked.Increment(ref _exportedBuffers);
		options.ExportItemsAttemptCallback = (retries, count) =>
		{
			if (retries == 0) Interlocked.Add(ref _items, count);
		};
		options.ExportRetryCallback = _ => Interlocked.Increment(ref _retries);
		options.ExportResponseCallback = (_, _) => Interlocked.Increment(ref _responses);
		options.ExportMaxRetriesCallback = _ => Interlocked.Increment(ref _maxRetriesExceeded);
		options.PublishToInboundChannelCallback = () => Interlocked.Increment(ref _inboundPublishes);
		options.PublishToInboundChannelFailureCallback = () => Interlocked.Increment(ref _inboundPublishFailures);
		options.PublishToOutboundChannelCallback = () => Interlocked.Increment(ref _outboundPublishes);
		options.PublishToOutboundChannelFailureCallback = () => Interlocked.Increment(ref _outboundPublishFailures);
		options.InboundChannelStartedCallback = () => _inboundChannelStarted = true;
		options.OutboundChannelStartedCallback = () => _outboundChannelStarted = true;
		options.OutboundChannelExitedCallback = () => _outboundChannelExited = true;

		if (options.ExportExceptionCallback == null) options.ExportExceptionCallback = e => ObservedException ??= e;
		else options.ExportExceptionCallback += e => ObservedException ??= e;
		return this;
	}

	protected virtual string AdditionalData => string.Empty;

	public override string ToString() => $@"{(!PublishSuccess ? "Failed" : "Successful")} publish over channel: {_name ?? nameof(ChannelListener<TEvent, TResponse>)}.
Exported Buffers: {_exportedBuffers:N0}
Exported Items: {_items:N0}
Export Responses: {_responses:N0}
Export Retries: {_retries:N0}
Export Exhausts: {_maxRetriesExceeded:N0}
Inbound Buffer Read Loop Started: {_inboundChannelStarted}
Inbound Buffer Publishes: {_inboundPublishes:N0}
Inbound Buffer Publish Failures: {_inboundPublishFailures:N0}
Outbound Buffer Read Loop Started: {_outboundChannelStarted}
Outbound Buffer Read Loop Exited: {_outboundChannelExited}
Outbound Buffer Publishes: {_outboundPublishes:N0}
Outbound Buffer Publish Failures: {_outboundPublishes:N0}
{AdditionalData}
Exception: {ObservedException}
";
}
