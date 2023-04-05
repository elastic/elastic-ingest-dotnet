// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Threading;
using Elastic.Channels.Buffers;

namespace Elastic.Channels.Diagnostics;

/// <summary>
/// Marker interface used by <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/> to improve
/// its to string if a <see cref="ChannelDiagnosticsListener{TEvent,TResponse}"/> gets injected.
/// </summary>
internal interface IChannelDiagnosticsListener {}

/// <summary>
/// A very rudimentary diagnostics object tracking various important metrics to provide insights into the
/// machinery of <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/>.
/// <para>For now this implementation only aids in better ToString() for <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/></para>
/// </summary>
public class ChannelDiagnosticsListener<TEvent, TResponse> : IChannelCallbacks<TEvent, TResponse>, IChannelDiagnosticsListener
{
	private readonly string? _name;
	private int _exportedBuffers;

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
	private bool _returnedRetryableObjects;

	/// <inheritdoc cref="ChannelDiagnosticsListener{TEvent,TResponse}"/>
	public ChannelDiagnosticsListener(string? name = null)
	{
		_name = name;
		ExportBufferCallback = () => Interlocked.Increment(ref _exportedBuffers);
		ExportItemsAttemptCallback = (retries, count) =>
		{
			if (retries == 0) Interlocked.Add(ref _items, count);
		};
		ExportRetryCallback = _ => Interlocked.Increment(ref _retries);
		ExportResponseCallback = (_, _) => Interlocked.Increment(ref _responses);
		ExportMaxRetriesCallback = _ => Interlocked.Increment(ref _maxRetriesExceeded);
		PublishToInboundChannelCallback = () => Interlocked.Increment(ref _inboundPublishes);
		PublishToInboundChannelFailureCallback = () => Interlocked.Increment(ref _inboundPublishFailures);
		PublishToOutboundChannelCallback = () => Interlocked.Increment(ref _outboundPublishes);
		PublishToOutboundChannelFailureCallback = () => Interlocked.Increment(ref _outboundPublishFailures);
		InboundChannelStartedCallback = () => _inboundChannelStarted = true;
		OutboundChannelStartedCallback = () => _outboundChannelStarted = true;
		OutboundChannelExitedCallback = () => _outboundChannelExited = true;

		ExportExceptionCallback = e => ObservedException ??= e;
		ExportRetryableCountCallback = i => _returnedRetryableObjects = true;
	}

	/// <summary>
	/// Keeps track of the first observed exception to calls to <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.Export"/>
	/// </summary>
	public Exception? ObservedException { get; private set; }

	/// <summary> Indicates if the overall publishing was successful</summary>
	public bool PublishSuccess => !_returnedRetryableObjects && ObservedException == null && _exportedBuffers > 0 && _maxRetriesExceeded == 0 && _items > 0;

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.ExportExceptionCallback"/>
	public Action<Exception>? ExportExceptionCallback { get; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.ExportItemsAttemptCallback"/>
	public Action<int, int>? ExportItemsAttemptCallback { get; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.ExportMaxRetriesCallback"/>
	public Action<IReadOnlyCollection<TEvent>>? ExportMaxRetriesCallback { get; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.ExportRetryCallback"/>
	public Action<IReadOnlyCollection<TEvent>>? ExportRetryCallback { get; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.ExportResponseCallback"/>
	public Action<TResponse, IWriteTrackingBuffer>? ExportResponseCallback { get; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.ExportBufferCallback"/>
	public Action? ExportBufferCallback { get; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.ExportRetryableCountCallback"/>
	public Action<int>? ExportRetryableCountCallback { get; set; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.PublishToInboundChannelCallback"/>
	public Action? PublishToInboundChannelCallback { get; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.PublishToInboundChannelFailureCallback"/>
	public Action? PublishToInboundChannelFailureCallback { get; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.PublishToOutboundChannelCallback"/>
	public Action? PublishToOutboundChannelCallback { get; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.OutboundChannelStartedCallback"/>
	public Action? OutboundChannelStartedCallback { get; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.OutboundChannelExitedCallback"/>
	public Action? OutboundChannelExitedCallback { get; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.InboundChannelStartedCallback"/>
	public Action? InboundChannelStartedCallback { get; }

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.PublishToOutboundChannelFailureCallback"/>
	public Action? PublishToOutboundChannelFailureCallback { get; }

	/// <summary>
	/// Provides a debug message to give insights to the machinery of <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/>
	/// </summary>
	public override string ToString() =>
		$@"{(!PublishSuccess ? "Failed" : "Successful")} publish over channel: {_name ?? "NAME NOT PROVIDED"}.
Exported Buffers: {_exportedBuffers:N0}
Exported Items: {_items:N0}
Export Responses: {_responses:N0}
Export Retries: {_retries:N0}
Export Exhausts: {_maxRetriesExceeded:N0}
Export Returned Items to retry: {_returnedRetryableObjects}
Inbound Buffer Read Loop Started: {_inboundChannelStarted}
Inbound Buffer Publishes: {_inboundPublishes:N0}
Inbound Buffer Publish Failures: {_inboundPublishFailures:N0}
Outbound Buffer Read Loop Started: {_outboundChannelStarted}
Outbound Buffer Read Loop Exited: {_outboundChannelExited}
Outbound Buffer Publishes: {_outboundPublishes:N0}
Outbound Buffer Publish Failures: {_outboundPublishFailures:N0}
Exception: {(ObservedException != null ? ObservedException.ToString() : "None")}
";

}
