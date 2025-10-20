// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Collections.Generic;
using Elastic.Channels.Diagnostics;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Ingest.Transport;

namespace Elastic.Ingest.Elasticsearch.Indices;

/// <summary> A channel to push messages to an Elasticsearch index
/// <para>If unsure prefer to use <see cref="DataStreamChannel{TEvent}"/></para>
/// </summary>
public class IndexChannel<TEvent> : IndexChannel<TEvent, IndexChannelOptions<TEvent>>
	where TEvent : class
{
	/// <inheritdoc cref="IndexChannel{TEvent}"/>
	public IndexChannel(IndexChannelOptions<TEvent> options) : base(options) { }

	/// <inheritdoc cref="IndexChannel{TEvent}"/>
	public IndexChannel(IndexChannelOptions<TEvent> options, ICollection<IChannelCallbacks<TEvent, BulkResponse>>? callbackListeners) : base(options, callbackListeners) { }
}

/// <inheritdoc cref="IndexChannel{TEvent}"/>
public class IndexChannel<TEvent, TChannelOptions> : ElasticsearchChannelBase<TEvent, TChannelOptions>
	where TChannelOptions : IndexChannelOptions<TEvent>
	where TEvent : class
{
	private readonly string _url;

	private readonly bool _skipIndexNameOnOperations;

	/// <inheritdoc cref="IndexChannel{TEvent}"/>
	public IndexChannel(TChannelOptions options) : this(options, null) { }

	/// <inheritdoc cref="IndexChannel{TEvent}"/>
	public IndexChannel(TChannelOptions options, ICollection<IChannelCallbacks<TEvent, BulkResponse>>? callbackListeners) : base(options, callbackListeners)
	{
		_url = base.BulkPathAndQuery;

		// When the configured index format represents a fixed index name, we can optimize by providing a URL with the target index specified.
		// We can later avoid the overhead of calculating and adding the index name to the operation headers.
		if (string.Format(Options.IndexFormat, DateTimeOffset.UtcNow).Equals(Options.IndexFormat, StringComparison.Ordinal))
		{
			_url = $"{Options.IndexFormat}/{base.BulkPathAndQuery}";
			_skipIndexNameOnOperations = true;
		}

		TemplateName = string.Format(Options.IndexFormat, "template");
		TemplateWildcard = string.Format(Options.IndexFormat, "*");
	}

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.RefreshTargets"/>
	protected override string RefreshTargets => _skipIndexNameOnOperations ? Options.IndexFormat : string.Format(Options.IndexFormat, "*");

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent, TChannelOptions}.BulkPathAndQuery"/>
	protected override string BulkPathAndQuery => _url;

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.CreateBulkOperationHeader"/>
	protected override BulkOperationHeader CreateBulkOperationHeader(TEvent @event) =>
		BulkRequestDataFactory.CreateBulkOperationHeaderForIndex(@event, ChannelHash, Options, _skipIndexNameOnOperations);

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.TemplateName"/>
	protected override string TemplateName { get; }

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.TemplateWildcard"/>
	protected override string TemplateWildcard { get; }

	/// <summary>
	/// Gets a default index template for the current <see cref="IndexChannel{TEvent}"/>
	/// </summary>
	/// <returns>A tuple of (name, body) describing the index template</returns>
	protected override (string, string) GetDefaultIndexTemplate(string name, string match, string mappingsName, string settingsName, string hash)
	{
		var indexTemplateBody = @$"{{
                ""index_patterns"": [""{match}""],
                ""composed_of"": [ ""{mappingsName}"", ""{settingsName}"" ],
                ""priority"": 201,
                ""_meta"": {{
                    ""description"": ""Template installed by .NET ingest libraries (https://github.com/elastic/elastic-ingest-dotnet)"",
                    ""assembly_version"": ""{LibraryVersion.Current}"",
                    ""hash"": ""{hash}""
                }}
            }}";
		return (name, indexTemplateBody);
	}

}
