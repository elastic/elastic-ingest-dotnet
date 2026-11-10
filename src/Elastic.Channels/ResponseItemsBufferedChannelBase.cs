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
	protected ResponseItemsBufferedChannelBase(TChannelOptions options, ICollection<IChannelCallbacks<TEvent, TResponse>>? callbackListeners)
		: base(options, callbackListeners) { }

	/// <inheritdoc cref="ResponseItemsBufferedChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}"/>
	protected ResponseItemsBufferedChannelBase(TChannelOptions options)
		: base(options) { }

	/// <summary> Based on <typeparamref name="TResponse"/> should return a bool indicating if retry is needed</summary>
	protected abstract bool Retry(TResponse response);

	/// <summary> Indicates that ALL items have to be retried in which case no further special handling is needed</summary>
	protected abstract bool RetryAllItems(TResponse response);

	/// <summary>
	/// Whether the response says every item succeeded, which makes matching items to events pointless: nothing is retried or rejected.
	/// <para>Defaults to <c>false</c>, which matches every item. Override it when the response carries a summary you can trust.</para>
	/// </summary>
	protected virtual bool AllItemsSucceeded(TResponse response) => false;

	/// <summary>
	/// Implementers have to implement this to align sent <typeparamref name="TEvent"/> to received <typeparamref name="TBulkResponseItem"/>'s
	/// </summary>
	protected abstract List<(TEvent, TBulkResponseItem)> Zip(TResponse response, IReadOnlyCollection<TEvent> page);

	/// <summary> A predicate indicating if a certain event needs to be retried </summary>
	protected abstract bool RetryEvent((TEvent, TBulkResponseItem) item);

	/// <summary>
	/// A predicate indicating an event was fully rejected and should be reported to
	/// <see cref="ResponseItemsChannelOptionsBase{TEvent,TResponse,TBulkResponseItem}.ServerRejectionCallback"/>
	/// </summary>
	protected abstract bool RejectEvent((TEvent, TBulkResponseItem) item);

	/// <inheritdoc cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.RetryBuffer"/>>
	protected override ArraySegment<TEvent> RetryBuffer(TResponse response, ArraySegment<TEvent> currentBuffer,
		IWriteTrackingBuffer statistics
	)
	{
		if (!Retry(response))
		{
			Options.ExportMaxRetriesCallback?.Invoke(currentBuffer);
			return EmptyArraySegments<TEvent>.Empty;
		}

		var backOffWholeRequest = RetryAllItems(response);

		// if we are not retrying the whole request find out if individual items need retrying
		if (backOffWholeRequest) return currentBuffer;

		// the usual case, and nothing to match
		if (AllItemsSucceeded(response))
			return EmptyArraySegments<TEvent>.Empty;

		var zipped = Zip(response, currentBuffer);
		if (zipped.Count == 0)
			return EmptyArraySegments<TEvent>.Empty;

		// One pass: an item is retried, or else rejected when the caller wants to hear about rejections.
		List<TEvent>? retryEvents = null;
		List<(TEvent, TBulkResponseItem)>? rejected = Options.ServerRejectionCallback != null ? new() : null;
		foreach (var pair in zipped)
		{
			if (RetryEvent(pair))
				(retryEvents ??= new List<TEvent>()).Add(pair.Item1);
			else if (rejected != null && RejectEvent(pair))
				rejected.Add(pair);
		}

		// report any events that are going to be dropped
		if (rejected is { Count: > 0 })
			Options.ServerRejectionCallback!(rejected);

		return retryEvents == null ? EmptyArraySegments<TEvent>.Empty : new ArraySegment<TEvent>(retryEvents.ToArray());
	}
}
