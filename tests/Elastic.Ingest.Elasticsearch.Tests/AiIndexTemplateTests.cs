// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.Strategies;
using Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;
using Elastic.Ingest.Transport;
using Elastic.Mapping;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class AiIndexTemplateTests
{
	private sealed class IndexStep : IndexTemplateStep
	{
		public string Body(BootstrapContext context) => BuildIndexTemplateBody(context);
	}

	private sealed class DataStreamStep : DataStreamTemplateStep
	{
		public string Body(BootstrapContext context) => BuildIndexTemplateBody(context);
	}

	private sealed class RecordingBootstrap : IBootstrapStrategy
	{
		public BootstrapContext Context { get; private set; }

		public IReadOnlyList<IBootstrapStep> Steps => [];

		public Task<bool> BootstrapAsync(BootstrapContext context, CancellationToken ctx = default)
		{
			Context = context;
			return Task.FromResult(true);
		}

		public bool Bootstrap(BootstrapContext context)
		{
			Context = context;
			return true;
		}
	}

	private static BootstrapContext Context(bool aiIndex) =>
		new()
		{
			Transport = TestSetup.SharedTransport,
			TemplateName = "docs-template",
			TemplateWildcard = "docs*",
			ChannelHash = "abc123",
			DataStreamType = "logs",
			AiIndex = aiIndex
		};

	private static string[] ComposedOf(string body) =>
		JsonNode.Parse(body)!["composed_of"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();

	[Test]
	public void IndexTemplateAiIndexPutsTheAiIndexComponentsAroundTheGeneratedOnes()
	{
		var body = new IndexStep().Body(Context(aiIndex: true));

		ComposedOf(body).Should().Equal(
			"ai-index@mappings", "docs-template-mappings", "docs-template-settings", "ai-index@custom");
	}

	[Test]
	public void IndexTemplateAiIndexOutranksElasticsearchsOwnAiIndexTemplate()
	{
		var node = JsonNode.Parse(new IndexStep().Body(Context(aiIndex: true)))!;

		node["priority"]!.GetValue<int>().Should().Be(501, "Elasticsearch's ai-index-idx-* templates use 500");
	}

	[Test]
	public void IndexTemplateAiIndexToleratesMissingAiIndexComponents()
	{
		var node = JsonNode.Parse(new IndexStep().Body(Context(aiIndex: true)))!;

		node["ignore_missing_component_templates"]!.AsArray().Select(n => n!.GetValue<string>())
			.Should().BeEquivalentTo("ai-index@mappings", "ai-index@custom");
	}

	[Test]
	public void IndexTemplateDefaultIsUnchanged()
	{
		var body = new IndexStep().Body(Context(aiIndex: false));

		body.Should().Be(
			@$"{{
                ""index_patterns"": [""docs*""],
                ""composed_of"": [ ""docs-template-mappings"", ""docs-template-settings"" ],
                ""priority"": 201,
                ""_meta"": {{
                    ""description"": ""Template installed by .NET ingest libraries (https://github.com/elastic/elastic-ingest-dotnet)"",
                    ""assembly_version"": ""{LibraryVersion.Current}"",
                    ""hash"": ""abc123""
                }}
            }}");
	}

	[Test]
	public void DataStreamTemplateAiIndexPutsTheAiIndexComponentsAroundTheGeneratedOnes()
	{
		var node = JsonNode.Parse(new DataStreamStep().Body(Context(aiIndex: true)))!;

		node["data_stream"].Should().NotBeNull();
		node["priority"]!.GetValue<int>().Should().Be(501);
		ComposedOf(node.ToJsonString()).Should().Equal(
			"ai-index@mappings", "docs-template-mappings", "docs-template-settings",
			"data-streams-mappings", "logs-settings", "logs-mappings", "ai-index@custom");
		node["ignore_missing_component_templates"]!.AsArray().Should().HaveCount(2);
	}

	[Test]
	public void DataStreamTemplateDefaultIsUnchanged()
	{
		var node = JsonNode.Parse(new DataStreamStep().Body(Context(aiIndex: false)))!;

		node["priority"]!.GetValue<int>().Should().Be(201);
		node["ignore_missing_component_templates"].Should().BeNull();
		ComposedOf(node.ToJsonString()).Should().Equal(
			"docs-template-mappings", "docs-template-settings",
			"data-streams-mappings", "logs-settings", "logs-mappings");
	}

	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public void ChannelPassesTheTypeContextsAiIndexFlagToBootstrap(bool aiIndex)
	{
		var typeContext = new ElasticsearchTypeContext(
			() => "{}",
			() => "{}",
			() => "{}",
			"hash",
			"settings-hash",
			"mappings-hash",
			new IndexStrategy { WriteTarget = "ai-index-idx-docs" },
			new SearchStrategy(),
			EntityTarget.Index,
			MappedType: typeof(TestDocument),
			AiIndex: aiIndex
		);
		var real = IngestStrategies.ForContext<TestDocument>(typeContext);
		var recording = new RecordingBootstrap();
		var strategy = new IngestStrategy<TestDocument>(
			real.TemplateName, real.TemplateWildcard, recording, real.DocumentIngest, real.Provisioning, real.AliasStrategy);
		var options = new IngestChannelOptions<TestDocument>(TestSetup.SharedTransport, strategy, typeContext);

		using var channel = new IngestChannel<TestDocument>(options);
		channel.BootstrapElasticsearch(BootstrapMethod.Failure);

		recording.Context.Should().NotBeNull();
		recording.Context.AiIndex.Should().Be(aiIndex);
	}
}
