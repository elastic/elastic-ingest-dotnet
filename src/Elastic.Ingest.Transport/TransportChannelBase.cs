// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Channels.Diagnostics;
using Elastic.Transport;

namespace Elastic.Ingest.Transport
{
	/// <summary>
	/// A <see cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}"/> implementation that provides a common base for channels
	/// looking to <see cref="Export(Elastic.Transport.HttpTransport,System.Collections.Generic.IReadOnlyCollection{TEvent},System.Threading.CancellationToken)"/> data
	/// over <see cref="HttpTransport"/>
	/// </summary>
	public abstract class TransportChannelBase<TChannelOptions, TEvent, TResponse, TBulkResponseItem> :
		ResponseItemsBufferedChannelBase<TChannelOptions, TEvent, TResponse, TBulkResponseItem>
		where TChannelOptions : TransportChannelOptionsBase<TEvent, TResponse, TBulkResponseItem>
		where TResponse : TransportResponse, new()

	{
		/// <inheritdoc cref="TransportChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}"/>
		protected TransportChannelBase(TChannelOptions options, ICollection<IChannelCallbacks<TEvent, TResponse>>? callbackListeners)
			: base(options, callbackListeners) { }

		/// <inheritdoc cref="TransportChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}"/>
		protected TransportChannelBase(TChannelOptions options)
			: base(options) { }

		/// <summary> Implement sending the current <paramref name="page"/> of the buffer to the output. </summary>
		/// <param name="transport"></param>
		/// <param name="page">Active page of the buffer that needs to be send to the output</param>
		/// <param name="ctx"></param>
		protected abstract Task<TResponse> Export(HttpTransport transport, IReadOnlyCollection<TEvent> page, CancellationToken ctx = default);

		/// <inheritdoc cref="BufferedChannelBase{TChannelOptions,TEvent,TResponse}.Export"/>>
		protected override Task<TResponse> Export(IReadOnlyCollection<TEvent> buffer, CancellationToken ctx = default) => Export(Options.Transport, buffer, ctx);
	}
}
