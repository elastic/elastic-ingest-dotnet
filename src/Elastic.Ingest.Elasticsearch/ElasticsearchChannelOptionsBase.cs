// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Ingest.Transport;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// Base options implementation for <see cref="ElasticsearchChannelBase{TEvent,TChannelOptions}"/> implementations
/// </summary>
public abstract class ElasticsearchChannelOptionsBase<TEvent> : TransportChannelOptionsBase<TEvent, BulkResponse, BulkResponseItem>
{
	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}"/>
	protected ElasticsearchChannelOptionsBase(ITransport transport) : base(transport) { }

	/// <summary>
	/// Export option, Optionally provide a custom write implementation for <typeparamref name="TEvent"/>
	/// </summary>
	public IElasticsearchEventWriter<TEvent>? EventWriter { get; set; }

	#if NETSTANDARD2_1_OR_GREATER
	/// <summary>
	/// Expert option,
	/// This will eagerly serialize to <see cref="ReadOnlyMemory{TEvent}"/> and use <see cref="PostData.ReadOnlyMemory"/>.
	/// If false (default) the channel will use <see cref="PostData.StreamHandler{T}"/> to directly write to the stream.
	/// </summary>
	#else
	/// <summary>
	/// Expert option, only available in netstandard2.1+ compatible runtimes to evaluate serialization approaches
	/// </summary>
	#endif
	[Obsolete("Temporary exposed expert option, used to evaluate two different approaches to serialization")]
	public bool UseReadOnlyMemory { get; set; }

}
