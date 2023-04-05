// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Collections.Generic;
using Elastic.Channels.Diagnostics;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Ingest.Transport;

namespace Elastic.Ingest.Elasticsearch.Indices
{
	/// <summary> A channel to push messages to an Elasticsearch index
	/// <para>If unsure prefer to use <see cref="DataStreamChannel{TEvent}"/></para>
	/// </summary>
	public class IndexChannel<TEvent> : ElasticsearchChannelBase<TEvent, IndexChannelOptions<TEvent>>
	{
		/// <inheritdoc cref="IndexChannel{TEvent}"/>
		public IndexChannel(IndexChannelOptions<TEvent> options) : this(options, null) { }

		/// <inheritdoc cref="IndexChannel{TEvent}"/>
		public IndexChannel(IndexChannelOptions<TEvent> options, ICollection<IChannelCallbacks<TEvent, BulkResponse>>? callbackListeners) : base(options, callbackListeners)
		{
			TemplateName = string.Format(Options.IndexFormat, "template");
			TemplateWildcard = string.Format(Options.IndexFormat, "*");
		}

		/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.CreateBulkOperationHeader"/>
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
}
