// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using Elastic.Ingest.Elasticsearch.Serialization;

namespace Elastic.Ingest.Elasticsearch.Indices
{
	public class IndexChannel<TEvent> : ElasticsearchChannelBase<TEvent, IndexResponseItemsChannelOptions<TEvent>>
	{
		public IndexChannel(IndexResponseItemsChannelOptions<TEvent> options) : base(options) { }

		protected override BulkOperationHeader CreateBulkOperationHeader(TEvent @event)
		{
			var indexTime = Options.TimestampLookup?.Invoke(@event) ?? DateTimeOffset.Now;
			if (Options.IndexOffset.HasValue) indexTime = indexTime.ToOffset(Options.IndexOffset.Value);

			var index = string.Format(Options.IndexFormat, indexTime);
			var id = Options.BulkOperationIdLookup?.Invoke(@event);
			return
				!string.IsNullOrWhiteSpace(id)
					? new IndexOperation { Index = index, Id = id }
					: new CreateOperation { Index = index };
		}
	}
}
