// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using Elastic.Channels.Buffers;

namespace Elastic.Channels.Diagnostics;

/// <summary>
/// A set of callbacks that implementers can inject into the channel without fear of overwriting the callbacks
/// defined in <see cref="ChannelOptionsBase{TEvent,TResponse}"/>
/// </summary>
public interface IChannelCallbacks<in TEvent, in TResponse>
{
	/// <summary> Called if the call to <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.ExportAsync"/> throws. </summary>
	Action<Exception>? ExportExceptionCallback { get; }

	/// <summary> Called with (number of retries) (number of items to be exported) </summary>
	Action<int, int>? ExportItemsAttemptCallback { get; }

	/// <summary> Subscribe to be notified of events that are retryable but did not store correctly withing the boundaries of <see cref="Channels.BufferOptions.ExportMaxRetries"/></summary>
	Action<IReadOnlyCollection<TEvent>>? ExportMaxRetriesCallback { get; }

	/// <summary> Subscribe to be notified of events that are retryable but did not store correctly within the number of configured <see cref="Channels.BufferOptions.ExportMaxRetries"/></summary>
	Action<IReadOnlyCollection<TEvent>>? ExportRetryCallback { get; }

	/// <summary> A generic hook to be notified of any bulk request being initiated by <see cref="InboundBuffer{TEvent}"/> </summary>
	Action<TResponse, IWriteTrackingBuffer>? ExportResponseCallback { get; }

	/// <summary>Called everytime an event is written to the inbound channel </summary>
	Action? PublishToInboundChannelCallback { get; }

	/// <summary>Called everytime an event is not written to the inbound channel </summary>
	Action? PublishToInboundChannelFailureCallback { get; }

	/// <summary>Called everytime the inbound channel publishes to the outbound channel. </summary>
	Action? PublishToOutboundChannelCallback { get; }

	/// <summary> Called when the thread to read the outbound channel is started </summary>
	Action? OutboundChannelStartedCallback { get; }

	/// <summary> Called when the thread to read the outbound channel has exited</summary>
	Action? OutboundChannelExitedCallback { get; }

	/// <summary> Called when the thread to read the inbound channel has started</summary>
	Action? InboundChannelStartedCallback { get; }

	/// <summary>Called everytime the inbound channel fails to publish to the outbound channel. </summary>
	Action? PublishToOutboundChannelFailureCallback { get; }

	/// <summary>
	/// Called once after a buffer has been flushed, if the buffer is retried this callback is only called once
	/// all retries have been exhausted. Its called regardless of whether the call to <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.ExportAsync"/>
	/// succeeded.
	/// </summary>
	Action? ExportBufferCallback { get; }

	/// <summary>
	/// Called once if an export to external system returned items that needed retries
	/// <para>Unlike <see cref="ExportRetryCallback"/> this is called even if no retries will be attempted
	///due to <see cref="BufferOptions.ExportMaxRetries"/> being 0.
	/// </para>
	/// </summary>
	public Action<int>? ExportRetryableCountCallback { get; }
}

internal class ChannelCallbackInvoker<TEvent, TResponse> : IChannelCallbacks<TEvent, TResponse>
{
	public ChannelCallbackInvoker(ICollection<IChannelCallbacks<TEvent, TResponse>> channelCallbacks)
	{
		ExportExceptionCallback = channelCallbacks
			.Select(e => e.ExportExceptionCallback)
			.Where(e => e != null)
			.Aggregate(ExportExceptionCallback, (s, f) => s + f);

		ExportItemsAttemptCallback = channelCallbacks
			.Select(e => e.ExportItemsAttemptCallback)
			.Where(e => e != null)
			.Aggregate(ExportItemsAttemptCallback, (s, f) => s + f);

		ExportMaxRetriesCallback = channelCallbacks
			.Select(e => e.ExportMaxRetriesCallback)
			.Where(e => e != null)
			.Aggregate(ExportMaxRetriesCallback, (s, f) => s + f);

		ExportRetryCallback = channelCallbacks
			.Select(e => e.ExportRetryCallback)
			.Where(e => e != null)
			.Aggregate(ExportRetryCallback, (s, f) => s + f);

		ExportResponseCallback = channelCallbacks
			.Select(e => e.ExportResponseCallback)
			.Where(e => e != null)
			.Aggregate(ExportResponseCallback, (s, f) => s + f);

		ExportBufferCallback = channelCallbacks
			.Select(e => e.ExportBufferCallback)
			.Where(e => e != null)
			.Aggregate((Action?)null, (s, f) => s + f);

		ExportRetryableCountCallback = channelCallbacks
			.Select(e => e.ExportRetryableCountCallback)
			.Where(e => e != null)
			.Aggregate(ExportRetryableCountCallback, (s, f) => s + f);


		PublishToInboundChannelCallback = channelCallbacks
			.Select(e => e.PublishToInboundChannelCallback)
			.Where(e => e != null)
			.Aggregate((Action?)null, (s, f) => s + f);

		PublishToInboundChannelFailureCallback = channelCallbacks
			.Select(e => e.PublishToInboundChannelFailureCallback)
			.Where(e => e != null)
			.Aggregate((Action?)null, (s, f) => s + f);

		PublishToOutboundChannelCallback = channelCallbacks
			.Select(e => e.PublishToOutboundChannelCallback)
			.Where(e => e != null)
			.Aggregate((Action?)null, (s, f) => s + f);

		OutboundChannelStartedCallback = channelCallbacks
			.Select(e => e.OutboundChannelStartedCallback)
			.Where(e => e != null)
			.Aggregate((Action?)null, (s, f) => s + f);

		OutboundChannelExitedCallback = channelCallbacks
			.Select(e => e.OutboundChannelExitedCallback)
			.Where(e => e != null)
			.Aggregate((Action?)null, (s, f) => s + f);

		InboundChannelStartedCallback = channelCallbacks
			.Select(e => e.InboundChannelStartedCallback)
			.Where(e => e != null)
			.Aggregate((Action?)null, (s, f) => s + f);

		PublishToOutboundChannelFailureCallback = channelCallbacks
			.Select(e => e.PublishToOutboundChannelFailureCallback)
			.Where(e => e != null)
			.Aggregate((Action?)null, (s, f) => s + f);
	}

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.ExportExceptionCallback"/>
	public Action<Exception>? ExportExceptionCallback { get; set; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.ExportItemsAttemptCallback"/>
	public Action<int, int>? ExportItemsAttemptCallback { get; set; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.ExportMaxRetriesCallback"/>
	public Action<IReadOnlyCollection<TEvent>>? ExportMaxRetriesCallback { get; set; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.ExportRetryCallback"/>
	public Action<IReadOnlyCollection<TEvent>>? ExportRetryCallback { get; set; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.ExportResponseCallback"/>
	public Action<TResponse, IWriteTrackingBuffer>? ExportResponseCallback { get; set; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.ExportBufferCallback"/>
	public Action? ExportBufferCallback { get; set; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.PublishToInboundChannelCallback"/>
	public Action? PublishToInboundChannelCallback { get; set; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.PublishToInboundChannelFailureCallback"/>
	public Action? PublishToInboundChannelFailureCallback { get; set; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.PublishToOutboundChannelCallback"/>
	public Action? PublishToOutboundChannelCallback { get; set; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.OutboundChannelStartedCallback"/>
	public Action? OutboundChannelStartedCallback { get; set; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.OutboundChannelExitedCallback"/>
	public Action? OutboundChannelExitedCallback { get; set; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.InboundChannelStartedCallback"/>
	public Action? InboundChannelStartedCallback { get; set; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.PublishToOutboundChannelFailureCallback"/>
	public Action? PublishToOutboundChannelFailureCallback { get; set; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.ExportRetryableCountCallback"/>
	public Action<int>? ExportRetryableCountCallback { get; set; }
}
