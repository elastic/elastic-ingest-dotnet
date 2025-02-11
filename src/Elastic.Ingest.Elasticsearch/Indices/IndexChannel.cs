// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Collections.Generic;
using Elastic.Channels.Diagnostics;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Ingest.Transport;
using static Elastic.Ingest.Elasticsearch.Serialization.HeaderSerializationStrategy;

namespace Elastic.Ingest.Elasticsearch.Indices;

/// <summary> A channel to push messages to an Elasticsearch index
/// <para>If unsure prefer to use <see cref="DataStreamChannel{TEvent}"/></para>
/// </summary>
public class IndexChannel<TEvent> : ElasticsearchChannelBase<TEvent, IndexChannelOptions<TEvent>>
	where TEvent : class
{
	private readonly bool _skipIndexNameOnOperations = false;
	private readonly string _url;

	/// <inheritdoc cref="IndexChannel{TEvent}"/>
	public IndexChannel(IndexChannelOptions<TEvent> options) : this(options, null) { }

	/// <inheritdoc cref="IndexChannel{TEvent}"/>
	public IndexChannel(IndexChannelOptions<TEvent> options, ICollection<IChannelCallbacks<TEvent, BulkResponse>>? callbackListeners, string? diagnosticsName = null)
		: base(options, callbackListeners, diagnosticsName ?? nameof(IndexChannel<TEvent>))
	{
		_url = base.BulkPathAndQuery;

		// When the configured index format represents a fixed index name, we can optimize by providing a URL with the target index specified.
		// We can later avoid the overhead of calculating and adding the index name to the operation headers.
		if (string.Format(Options.IndexFormat, DateTimeOffset.Now).Equals(Options.IndexFormat, StringComparison.Ordinal))
		{
			_url = $"{Options.IndexFormat}/{base.BulkPathAndQuery}";
			_skipIndexNameOnOperations = true;
		}

		TemplateName = string.Format(Options.IndexFormat, "template");
		TemplateWildcard = string.Format(Options.IndexFormat, "*");
	}

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent, TChannelOptions}.BulkPathAndQuery"/>
	protected override string BulkPathAndQuery => _url;

	/// <inheritdoc cref="EventIndexStrategy"/>
	protected override (HeaderSerializationStrategy, BulkHeader?) EventIndexStrategy(TEvent @event)
	{
		var indexTime = Options.TimestampLookup?.Invoke(@event) ?? DateTimeOffset.Now;
		if (Options.IndexOffset.HasValue) indexTime = indexTime.ToOffset(Options.IndexOffset.Value);

		var index = _skipIndexNameOnOperations ? string.Empty : string.Format(Options.IndexFormat, indexTime);
		var id = Options.BulkOperationIdLookup?.Invoke(@event);
		var templates = Options.DynamicTemplateLookup?.Invoke(@event);
		var requireAlias = Options.RequireAlias?.Invoke(@event);
		var listExecutedPipelines = Options.ListExecutedPipelines?.Invoke(@event);
		var isUpsert = Options.BulkUpsertLookup?.Invoke(@event, index) is true;
		if (string.IsNullOrWhiteSpace(index)
			&& string.IsNullOrWhiteSpace(id)
			&& templates is null
			&& isUpsert is false
			&& requireAlias is null or false
			&& listExecutedPipelines is null or false)
			return Options.OperationMode == OperationMode.Index
				? (IndexNoParams, null)
				: (CreateNoParams, null);

		var header = new BulkHeader
		{
			Id = id,
			Index = index,
			DynamicTemplates = templates,
			RequireAlias = requireAlias,
			ListExecutedPipelines = listExecutedPipelines
		};
		var op = Options.OperationMode == OperationMode.Index
			? HeaderSerializationStrategy.Index
			: Create;
		if (isUpsert)
			op = Update;

		return (op, header);
	}

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.TemplateName"/>
	protected override string TemplateName { get; }
	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.TemplateWildcard"/>
	protected override string TemplateWildcard { get; }

	/// <summary>
	/// Gets a default index template for the current <see cref="IndexChannel{TEvent}"/>
	/// </summary>
	/// <returns>A tuple of (name, body) describing the index template</returns>
	protected override (string, string) GetDefaultIndexTemplate(string name, string match, string mappingsName, string settingsName)
	{
		var indexTemplateBody = @$"{{
                ""index_patterns"": [""{match}""],
                ""composed_of"": [ ""{mappingsName}"", ""{settingsName}"" ],
                ""priority"": 201,
                ""_meta"": {{
                    ""description"": ""Template installed by .NET ingest libraries (https://github.com/elastic/elastic-ingest-dotnet)"",
                    ""assembly_version"": ""{LibraryVersion.Current}""
                }}
            }}";
		return (name, indexTemplateBody);
	}
}
