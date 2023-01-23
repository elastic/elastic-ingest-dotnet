// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.DataStreams
{
	public class DataStreamChannelOptions<TEvent> : ElasticsearchChannelOptionsBase<TEvent>
	{
		public DataStreamChannelOptions(HttpTransport transport) : base(transport) =>
			DataStream = new DataStreamName(typeof(TEvent).Name.ToLowerInvariant());

		public DataStreamName DataStream { get; set; }
	}
}
