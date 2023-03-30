// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels.Buffers;

namespace Elastic.Channels
{
	/// <summary>
	///
	/// </summary>
	/// <typeparam name="TEvent"></typeparam>
	/// <typeparam name="TResponse"></typeparam>
	public abstract class ChannelOptionsBase<TEvent, TResponse>
	{
		/// <inheritdoc cref="BufferOptions"/>
		public BufferOptions BufferOptions { get; set; } = new ();

		/// <summary> Called if the call to <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.Export"/> throws. </summary>
		public Action<Exception>? ExportExceptionCallback { get; set; }

		/// <summary> Called with (number of retries) (number of items to be exported) </summary>
		public Action<int, int>? ExportItemsAttemptCallback { get; set; }

		/// <summary> Subscribe to be notified of events that are retryable but did not store correctly withing the boundaries of <see cref="Channels.BufferOptions.ExportMaxRetries"/></summary>
		public Action<IReadOnlyCollection<TEvent>>? ExportMaxRetriesCallback { get; set; }

		/// <summary> Subscribe to be notified of events that are retryable but did not store correctly within the number of configured <see cref="Channels.BufferOptions.ExportMaxRetries"/></summary>
		public Action<IReadOnlyCollection<TEvent>>? ExportRetryCallback { get; set; }

		/// <summary> A generic hook to be notified of any bulk request being initiated by <see cref="InboundBuffer{TEvent}"/> </summary>
		public Action<TResponse, IWriteTrackingBuffer>? ExportResponseCallback { get; set; }

		/// <summary>Called everytime an event is written to the inbound channel </summary>
		public Action? PublishToInboundChannelCallback { get; set; }

		/// <summary>Called everytime an event is not written to the inbound channel </summary>
		public Action? PublishToInboundChannelFailureCallback { get; set; }

		/// <summary>Called everytime the inbound channel publishes to the outbound channel. </summary>
		public Action? PublishToOutboundChannelCallback { get; set; }

		/// <summary> Called when the thread to read the outbound channel is started </summary>
		public Action? OutboundChannelStartedCallback { get; set; }
		/// <summary> Called when the thread to read the outbound channel has exited</summary>
		public Action? OutboundChannelExitedCallback { get; set; }

		/// <summary> Called when the thread to read the inbound channel has started</summary>
		public Action? InboundChannelStartedCallback { get; set; }

		/// <summary>Called everytime the inbound channel fails to publish to the outbound channel. </summary>
		public Action? PublishToOutboundChannelFailureCallback { get; set; }
	}

}
