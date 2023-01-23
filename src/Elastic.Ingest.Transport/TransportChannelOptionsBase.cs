// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Channels;
using Elastic.Transport;

namespace Elastic.Ingest.Transport
{
	public abstract class TransportResponseItemsChannelOptionsBase<TEvent, TResponse, TResponseItem, TBuffer>
		: ResponseItemsChannelOptionsBase<TEvent, TBuffer, TResponse, TResponseItem>
		where TBuffer : BufferOptions<TEvent>, new()
	{
		protected TransportResponseItemsChannelOptionsBase(HttpTransport transport) => Transport = transport;

		public HttpTransport Transport { get; }
	}
}
