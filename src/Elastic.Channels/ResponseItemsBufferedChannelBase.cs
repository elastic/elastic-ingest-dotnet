// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using Elastic.Channels.Buffers;
using Elastic.Channels.Diagnostics;

namespace Elastic.Channels;

/// <summary>
/// Channel options to <see cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}"/>
/// </summary>
public abstract class ResponseItemsChannelOptionsBase<TEvent, TResponse, TBulkResponseItem>
	: ChannelOptionsBase<TEvent, TResponse>
{
	/// <summary> Subscribe to be notified of events that can not be stored in Elasticsearch</summary>
	public Action<List<(TEvent, TBulkResponseItem)>>? ServerRejectionCallback { get; set; }
}

/// <summary>
/// A specialized implementation of <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/>
/// <para>This base class exist to help with cases where writing data in bulk to a receiver is capable of reporting back
/// individual write failures.
/// </para>
/// </summary>
public abstract class ResponseItemsBufferedChannelBase<TChannelOptions, TEvent, TResponse, TBulkResponseItem>
	: BufferedChannelBase<TChannelOptions, TEvent, TResponse>
	where TChannelOptions : ResponseItemsChannelOptionsBase<TEvent, TResponse, TBulkResponseItem>
	where TResponse : class, new()
	where TEvent : class
{
	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}"/>
	protected ResponseItemsBufferedChannelBase(TChannelOptions options, ICollection<IChannelCallbacks<TEvent, TResponse>>? callbackListeners, string diagnosticsName)
		: base(options, callbackListeners, diagnosticsName) { }

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}"/>
	protected ResponseItemsBufferedChannelBase(TChannelOptions options, string diagnosticsName)
		: base(options, diagnosticsName) { }

	/// <summary> Based on <typeparamref name="TResponse"/> should return a bool indicating if retry is needed</summary>
	protected abstract bool Retry(TResponse response);

	/// <summary> Indicates that ALL items have to be retried in which case no further special handling is needed</summary>
	protected abstract bool RetryAllItems(TResponse response);

	/// <summary>
	/// Implementers have to implement this to align sent <typeparamref name="TEvent"/> to received <typeparamref name="TBulkResponseItem"/>'s
	/// </summary>
	protected abstract List<(TEvent, TBulkResponseItem)> Zip(TResponse response, IReadOnlyCollection<TEvent> page);

	/// <summary> A predicate indicating if a certain event needs to be retried </summary>
	protected abstract bool RetryEvent((TEvent, TBulkResponseItem) @event);

	/// <summary>
	/// A predicate indicating an event was fully rejected and should be reported to
	/// <see cref="ResponseItemsChannelOptionsBase{TEvent,TResponse,TBulkResponseItem}.ServerRejectionCallback"/>
	/// </summary>
	protected abstract bool RejectEvent((TEvent, TBulkResponseItem) @event);

	/// <inheritdoc cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.RetryBuffer"/>>
	protected override ArraySegment<TEvent> RetryBuffer(TResponse response, ArraySegment<TEvent> events,
		IWriteTrackingBuffer consumedBufferStatistics
	)
	{
		if (!Retry(response)) return EmptyArraySegments<TEvent>.Empty;

		var backOffWholeRequest = RetryAllItems(response);

		// if we are not retrying the whole request find out if individual items need retrying
		if (backOffWholeRequest) return events;

		var zipped = Zip(response, events);
		var retryEvents = zipped
			.Where(t => RetryEvent(t))
			.Select(t => t.Item1)
			.ToArray();

		// report any events that are going to be dropped
		if (Options.ServerRejectionCallback != null)
		{
			var rejected = zipped
				.Where(t => RejectEvent(t) && !RetryEvent(t))
				.ToList();
			if (rejected.Count > 0) Options.ServerRejectionCallback(rejected);
		}
		return new ArraySegment<TEvent>(retryEvents);
	}
}
