// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System.Collections.Generic;
using Elastic.Ingest.Elasticsearch.Serialization;

namespace Elastic.Ingest.Elasticsearch.DataStreams
{
	public class DataStreamChannel<TEvent> : ElasticsearchChannelBase<TEvent, DataStreamResponseItemsChannelOptions<TEvent>>
	{
		private readonly CreateOperation _fixedHeader;

		public DataStreamChannel(DataStreamResponseItemsChannelOptions<TEvent> options) : base(options)
		{
			var target = Options.DataStream.ToString();
			_fixedHeader = new CreateOperation { Index = target };
		}

		protected override BulkOperationHeader CreateBulkOperationHeader(TEvent @event) => _fixedHeader;

	}
}
