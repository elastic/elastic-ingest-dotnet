// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections;
using System.Collections.Generic;
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

	/// <summary> Optionally set dynamic templates for event</summary>
	public Func<TEvent, IDictionary<string, string>?>? DynamicTemplateLookup { get; set; }

	/// <summary> If true, the response will include the ingest pipelines that were executed. Defaults to false. </summary>
	public Func<TEvent, bool>? ListExecutedPipelines { get; set; }

}
