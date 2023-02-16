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

	private int _responses;
	private int _rejections;
	private int _retries;
	private int _items;
	private int _maxRetriesExceeded;
	private int _outboundPublishes;
	private int _outboundPublishFailures;

	// ReSharper disable once MemberCanBeProtected.Global
	public ChannelListener<TEvent, TResponse> Register(ChannelOptionsBase<TEvent, TResponse> options)
	{
		options.BufferOptions.BufferExportedCallback = () => Interlocked.Increment(ref _exportedBuffers);
		options.PublishRejectionCallback = _ => Interlocked.Increment(ref _rejections);
		options.ExportItemsAttemptCallback = (retries, count) =>
		{
			if (retries == 0) Interlocked.Add(ref _items, count);
		};
		options.ExportRetryCallback = _ => Interlocked.Increment(ref _retries);
		options.ExportResponseCallback = (_, _) => Interlocked.Increment(ref _responses);
		options.ExportMaxRetriesCallback = _ => Interlocked.Increment(ref _maxRetriesExceeded);
		options.PublishToOutboundChannel = () => Interlocked.Increment(ref _outboundPublishes);
		options.PublishToOutboundChannelFailure = () => Interlocked.Increment(ref _outboundPublishFailures);

		if (options.ExceptionCallback == null) options.ExceptionCallback = e => ObservedException ??= e;
		else options.ExceptionCallback += e => ObservedException ??= e;
		return this;
	}

	protected virtual string AdditionalData => string.Empty;

	public override string ToString() => $@"{(!PublishSuccess ? "Failed" : "Successful")} publish over channel: {_name ?? nameof(ChannelListener<TEvent, TResponse>)}.
Total Exported Buffers: {_exportedBuffers:N0}
Total Exported Items: {_items:N0}
Responses: {_responses:N0}
Inbound Buffer TryWrite failures: {_rejections:N0}
Outbound Buffer Publishes: {_outboundPublishes:N0}
Outbound Buffer Publish Failures: {_outboundPublishes:N0}
Send() Retries: {_retries:N0}
Send() Exhausts: {_maxRetriesExceeded:N0}{AdditionalData}
Exception: {ObservedException}
";
}
