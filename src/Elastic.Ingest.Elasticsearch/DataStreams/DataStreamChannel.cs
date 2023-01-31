// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Ingest.Transport;

namespace Elastic.Ingest.Elasticsearch.DataStreams
{
	public class DataStreamChannel<TEvent> : ElasticsearchChannelBase<TEvent, DataStreamChannelOptions<TEvent>>
	{
		private readonly CreateOperation _fixedHeader;

		public DataStreamChannel(DataStreamChannelOptions<TEvent> options) : base(options)
		{
			var target = Options.DataStream.ToString();
			_fixedHeader = new CreateOperation { Index = target };
		}

		protected override BulkOperationHeader CreateBulkOperationHeader(TEvent @event) => _fixedHeader;

		protected override string TemplateName => Options.DataStream.GetTemplateName();
		protected override string TemplateWildcard => Options.DataStream.GetNamespaceWildcard();

		/// <summary>
		/// Gets a default index template for the current <see cref="DataStreamChannel{TEvent}"/>
		/// </summary>
		/// <returns>A tuple of (name, body) describing the index template</returns>
		protected override (string, string) GetDefaultIndexTemplate(string name, string match, string mappingsName, string settingsName)
		{
			var indexTemplateBody = @$"{{
                ""index_patterns"": [""{match}""],
                ""data_stream"": {{ }},
                ""composed_of"": [ ""{mappingsName}"", ""{settingsName}"", ""data-streams-mappings"" ],
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
