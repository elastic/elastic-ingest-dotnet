// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;

namespace Elastic.Channels.Diagnostics;

public class ChannelListener<TEvent, TResponse>
{
	private int _bufferFlushCallback;

	public Exception? ObservedException { get; private set; }

	public virtual bool PublishSuccess => ObservedException == null && _bufferFlushCallback > 0 && _maxRetriesExceeded == 0 && _items > 0;

	private int _responses;
	private int _rejections;
	private int _retries;
	private int _items;
	private int _maxRetriesExceeded;
	private int _outboundWriteRetries;

	// ReSharper disable once MemberCanBeProtected.Global
	public ChannelListener<TEvent, TResponse> Register(ChannelOptionsBase<TEvent, TResponse> options)
	{
		options.BufferOptions.BufferFlushCallback = () => Interlocked.Increment(ref _bufferFlushCallback);
		options.ResponseCallback = (_, _) => Interlocked.Increment(ref _responses);
		options.PublishRejectionCallback = _ => Interlocked.Increment(ref _rejections);
		options.RetryCallBack = _ => Interlocked.Increment(ref _retries);
		options.BulkAttemptCallback = (retries, count) =>
		{
			if (retries == 0) Interlocked.Add(ref _items, count);
		};
		options.MaxRetriesExceededCallback = _ => Interlocked.Increment(ref _maxRetriesExceeded);
		options.OutboundChannelRetryCallback = _=> Interlocked.Increment(ref _outboundWriteRetries);

		if (options.ExceptionCallback == null) options.ExceptionCallback = e => ObservedException ??= e;
		else options.ExceptionCallback += e => ObservedException ??= e;
		return this;
	}

	protected virtual string AdditionalData => string.Empty;

	public override string ToString() => $@"{(!PublishSuccess ? "Failed" : "Successful")} publish over channel.
Consumed on outbound: {_items:N0}
Flushes: {_bufferFlushCallback:N0}
Responses: {_responses:N0}
Outbound Buffer TryWrite Retries: {_retries:N0}
Inbound Buffer TryWrite failures: {_rejections:N0}
Send() Retries: {_retries:N0}
Send() Exhausts: {_maxRetriesExceeded:N0}{AdditionalData}
Exception: {ObservedException}
";
}
