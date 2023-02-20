// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Channels;
using Elastic.Transport;

namespace Elastic.Ingest.Transport
{
	/// <summary>
	/// Provides channel options to <see cref="TransportChannelBase{TChannelOptions,TEvent,TResponse,TBulkResponseItem}"/> implementation.
	/// </summary>
	public abstract class TransportChannelOptionsBase<TEvent, TResponse, TResponseItem>
		: ResponseItemsChannelOptionsBase<TEvent, TResponse, TResponseItem>
	{
		/// <inheritdoc cref="TransportChannelOptionsBase{TEvent,TResponse,TResponseItem}"/>
		protected TransportChannelOptionsBase(HttpTransport transport) => Transport = transport;

		/// <summary>
		/// The <see cref="HttpTransport{TConfiguration}"/> implementation to be used by the channel
		/// </summary>
		public HttpTransport Transport { get; }
	}
}
