// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Channels.Diagnostics;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Ingest.Transport;
using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;
using static Elastic.Ingest.Elasticsearch.ElasticsearchChannelStatics;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// An abstract base class for both <see cref="DataStreamChannel{TEvent}"/> and <see cref="IndexChannel{TEvent}"/>
/// <para>Coordinates most of the sending to- and bootstrapping of Elasticsearch</para>
/// </summary>
public abstract partial class ElasticsearchChannelBase<TEvent, TChannelOptions>
	: TransportChannelBase<TChannelOptions, TEvent, BulkResponse, BulkResponseItem>
	where TChannelOptions : ElasticsearchChannelOptionsBase<TEvent>
{
	private Task SerializeHeaderAsync(Stream stream, ref readonly BulkHeader header, JsonSerializerOptions serializerOptions, CancellationToken ctx) =>
		throw new NotImplementedException();

	private Task SerializePlainIndexHeaderAsync(Stream stream, CancellationToken ctx) =>
		throw new NotImplementedException();
}
