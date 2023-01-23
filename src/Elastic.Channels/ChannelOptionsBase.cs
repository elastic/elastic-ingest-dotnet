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
		/// If <see cref="BufferOptions.MaxInFlightMessages"/> is reached, <see cref="TEvent"/>'s will fail to be published to the channel. You can be notified of dropped
		/// events with this callback
		/// </summary>
		public Action<TEvent>? PublishRejectionCallback { get; set; }

		public Action<Exception>? ExceptionCallback { get; set; }

		public Action<int, int>? BulkAttemptCallback { get; set; }

		/// <summary> Subscribe to be notified of events that are retryable but did not store correctly withing the boundaries of <see cref="MaxRetries"/></summary>
		public Action<IReadOnlyCollection<TEvent>>? MaxRetriesExceededCallback { get; set; }

		/// <summary> Subscribe to be notified of events that are retryable but did not store correctly within the number of configured <see cref="MaxRetries"/></summary>
		public Action<IReadOnlyCollection<TEvent>>? RetryCallBack { get; set; }

		/// <summary> A generic hook to be notified of any bulk request being initiated by <see cref="InboundBuffer{TEvent}"/> </summary>
		public Action<TResponse, IWriteTrackingBuffer> ResponseCallback { get; set; } = (r, b) => { };
	}

}
