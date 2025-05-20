// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Linq;
using Elastic.Channels.Diagnostics;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Ingest.Transport;

namespace Elastic.Ingest.Elasticsearch.DataStreams;

/// <summary> A channel to push messages to Elasticsearch data streams </summary>
public class DataStreamChannel<TEvent> : ElasticsearchChannelBase<TEvent, DataStreamChannelOptions<TEvent>>
	where TEvent : class
{
	private readonly CreateOperation _fixedHeader;
	private readonly string _url;

	/// <inheritdoc cref="DataStreamChannel{TEvent}"/>
	public DataStreamChannel(DataStreamChannelOptions<TEvent> options) : this(options, null) { }

	/// <inheritdoc cref="DataStreamChannel{TEvent}"/>
	public DataStreamChannel(DataStreamChannelOptions<TEvent> options, ICollection<IChannelCallbacks<TEvent, BulkResponse>>? callbackListeners) : base(options, callbackListeners)
	{
		var dataStream = Options.DataStream.ToString();

		_url = $"{dataStream}/{base.BulkPathAndQuery}";

		_fixedHeader = new CreateOperation();
	}

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.RefreshTargets"/>
	protected override string RefreshTargets => Options.DataStream.ToString();

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.CreateBulkOperationHeader"/>
	protected override BulkOperationHeader CreateBulkOperationHeader(TEvent @event) => _fixedHeader;

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.TemplateName"/>
	protected override string TemplateName => Options.DataStream.GetTemplateName();
	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent,TChannelOptions}.TemplateWildcard"/>
	protected override string TemplateWildcard => Options.DataStream.GetNamespaceWildcard();

	/// <inheritdoc cref="ElasticsearchChannelBase{TEvent, TChannelOptions}.BulkPathAndQuery"/>
	protected override string BulkPathAndQuery => _url;

	/// <summary>
	/// Gets a default index template for the current <see cref="DataStreamChannel{TEvent}"/>
	/// </summary>
	/// <returns>A tuple of (name, body) describing the index template</returns>
	protected override (string, string) GetDefaultIndexTemplate(string name, string match, string mappingsName, string settingsName)
	{
		var additionalComponents = GetInferredComponentTemplates();
		var additionalComponentsJson = string.Join(", ", additionalComponents.Select(a => $"\"{a}\""));

		var indexTemplateBody = @$"{{
                ""index_patterns"": [""{match}""],
                ""data_stream"": {{ }},
                ""composed_of"": [ ""{mappingsName}"", ""{settingsName}"", {additionalComponentsJson} ],
                ""priority"": 201,
                ""_meta"": {{
                    ""description"": ""Template installed by .NET ingest libraries (https://github.com/elastic/elastic-ingest-dotnet)"",
                    ""assembly_version"": ""{LibraryVersion.Current}""
                }}
            }}";
		return (name, indexTemplateBody);
	}

	/// <summary>
	/// Yields additional component templates to include in the index template based on the data stream naming scheme
	/// </summary>
	protected List<string> GetInferredComponentTemplates()
	{
		var additionalComponents = new List<string> { "data-streams-mappings" };
		// if we know the type of data is logs or metrics apply certain defaults that Elasticsearch ships with.
		if (Options.DataStream.Type.ToLowerInvariant() == "logs")
			additionalComponents.AddRange(new[] { "logs-settings", "logs-mappings" });
		else if (Options.DataStream.Type.ToLowerInvariant() == "metrics")
			additionalComponents.AddRange(new[] { "metrics-settings", "metrics-mappings" });
		return additionalComponents;
	}
}
