// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.DataStreams;

/// <summary>Controls which data stream the channel should write to</summary>
public class DataStreamChannelOptions<TEvent> : IngestChannelOptionsBase<TEvent>
{
	/// <inheritdoc cref="DataStreamChannelOptions{TEvent}"/>
	public DataStreamChannelOptions(ITransport transport) : base(transport) =>
		DataStream = new DataStreamName(typeof(TEvent).Name.ToLowerInvariant());

	/// <inheritdoc cref="DataStreamName"/>
	public DataStreamName DataStream { get; set; }
}
