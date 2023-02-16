// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Elastic.Channels
{
	/// <summary>
	///
	/// </summary>
	/// <typeparam name="TEvent"></typeparam>
	/// <typeparam name="TResponse"></typeparam>
	public abstract class ChannelOptionsBase<TEvent, TResponse>
	{
		public BufferOptions BufferOptions { get; set; } = new ();

		public Func<Stream, CancellationToken, TEvent, Task> WriteEvent { get; set; } = null!;

		/// <summary>
		/// If <see cref="Channels.BufferOptions.InboundBufferMaxSize"/> is reached, <see cref="TEvent"/>'s will fail to be published to the channel. You can be notified of dropped
		/// events with this callback
		/// </summary>
		public Action<TEvent>? PublishRejectionCallback { get; set; }

		public Action<Exception>? ExceptionCallback { get; set; }

		public Action<int, int>? ExportItemsAttemptCallback { get; set; }

		/// <summary> Subscribe to be notified of events that are retryable but did not store correctly withing the boundaries of <see cref="Channels.BufferOptions.ExportMaxRetries"/></summary>
		public Action<IReadOnlyCollection<TEvent>>? ExportMaxRetriesCallback { get; set; }

		/// <summary> Subscribe to be notified of events that are retryable but did not store correctly within the number of configured <see cref="Channels.BufferOptions.ExportMaxRetries"/></summary>
		public Action<IReadOnlyCollection<TEvent>>? ExportRetryCallback { get; set; }

		/// <summary> A generic hook to be notified of any bulk request being initiated by <see cref="InboundBuffer{TEvent}"/> </summary>
		public Action<TResponse, IWriteTrackingBuffer>? ExportResponseCallback { get; set; }

		/// <summary>Called everytime an event is written to the inbound channel </summary>
		public Action? PublishToInboundChannel { get; set; }

		/// <summary>Called everytime an event is not written to the inbound channel </summary>
		public Action? PublishToInboundChannelFailure { get; set; }

		/// <summary>Called everytime the inbound channel publishes to the outbound channel. </summary>
		public Action? PublishToOutboundChannel { get; set; }

		public Action? OutboundChannelStarted { get; set; }
		public Action? OutboundChannelExited { get; set; }
		public Action? InboundChannelStarted { get; set; }

		/// <summary>Called everytime the inbound channel fails to publish to the outbound channel. </summary>
		public Action? PublishToOutboundChannelFailure { get; set; }
	}

}
