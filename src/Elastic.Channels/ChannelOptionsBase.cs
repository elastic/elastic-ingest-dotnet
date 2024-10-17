// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Threading;
using Elastic.Channels.Buffers;
using Elastic.Channels.Diagnostics;

namespace Elastic.Channels;

/// <summary>
///
/// </summary>
/// <typeparam name="TEvent"></typeparam>
/// <typeparam name="TResponse"></typeparam>
public abstract class ChannelOptionsBase<TEvent, TResponse> : IChannelCallbacks<TEvent, TResponse>
{
	/// <inheritdoc cref="BufferOptions"/>
	public BufferOptions BufferOptions { get; set; } = new();

	/// <summary>
	/// TBD
	/// </summary>
	public Action<TEvent>? BufferItemDropped { get; set; }

	/// <summary>
	/// Ensures a <see cref="ChannelDiagnosticsListener{TEvent,TResponse}"/> gets registered so this <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/>
	/// implementation returns diagnostics in its <see cref="object.ToString"/> implementation
	/// </summary>
	public bool DisableDiagnostics { get; set; }

	/// <summary> Provide an external cancellation token </summary>
	public CancellationToken? CancellationToken { get; set; }

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

	/// <inheritdoc cref="IChannelCallbacks{TEvent,TResponse}.ExportRetryableCountCallback"/>
	public Action<int>? ExportRetryableCountCallback { get; set; }

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
}
