// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Indices;

/// <summary>
/// Provides options to <see cref="IndexChannel{TEvent}"/> to control how and where data gets written to Elasticsearch
/// </summary>
/// <typeparam name="TEvent"></typeparam>
public class IndexChannelOptions<TEvent> : ElasticsearchChannelOptionsBase<TEvent>
{
	/// <inheritdoc cref="IndexChannelOptions{TEvent}"/>
	public IndexChannelOptions(HttpTransport transport) : base(transport) { }

	/// <summary>
	/// Gets or sets the format string for the Elastic search index. The current <c>DateTimeOffset</c> is passed as parameter
	/// 0.
	/// <para> Defaults to "dotnet-{0:yyyy.MM.dd}"</para>
	/// <para> If no {0} parameter is defined the index name is effectively fixed</para>
	/// </summary>
	public string IndexFormat { get; set; } = "dotnet-{0:yyyy.MM.dd}";

	/// <summary>
	/// Gets or sets the offset to use for the index <c>DateTimeOffset</c>. Default value is null, which uses the system local
	/// offset. Use "00:00" for UTC.
	/// </summary>
	public TimeSpan? IndexOffset { get; set; }

	/// <summary>
	/// Provide a per document <c>DateTimeOffset</c> to be used as the date passed as parameter 0 to <see cref="IndexFormat"/>
	/// </summary>
	public Func<TEvent, DateTimeOffset?>? TimestampLookup { get; set; }

	/// <summary>
	/// If the document provides an Id this allows you to set a per document `_id`.
	/// <para>If an `_id` is defined an `_index` bulk operation will be created.</para>
	/// <para>Otherwise (the default) `_create` bulk operation will be issued for the document.</para>
	/// <para>Read more about bulk operations here:</para>
	/// <para>https://www.elastic.co/guide/en/elasticsearch/reference/current/docs-bulk.html#bulk-api-request-body</para>
	/// </summary>
	public Func<TEvent, string>? BulkOperationIdLookup { get; set; }

	/// <summary>
	/// Uses the callback provided to <see cref="BulkOperationIdLookup"/> to determine if this is in fact an update operation
	/// <para>If this returns true the document will be sent as an upsert operation</para>
	/// <para>Otherwise (the default) `index` bulk operation will be issued for the document.</para>
	/// <para>Read more about bulk operations here:</para>
	/// <para>https://www.elastic.co/guide/en/elasticsearch/reference/current/docs-bulk.html#bulk-api-request-body</para>
	/// </summary>
	public Func<TEvent, string, bool>? BulkUpsertLookup { get; set; }
}
