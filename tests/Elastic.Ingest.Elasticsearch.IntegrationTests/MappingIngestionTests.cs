// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Mapping;
using Elastic.Transport;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests;

/// <summary>
/// A document type with explicit Elastic.Mapping field type attributes.
/// [Id] drives upsert routing, [Keyword] maps to keyword, [Text] maps to text.
/// </summary>
public class MappedDoc
{
	[Id]
	[Keyword]
	[JsonPropertyName("id")]
	public string Id { get; set; } = null!;

	[Text]
	[JsonPropertyName("title")]
	public string Title { get; set; } = string.Empty;

	[Keyword]
	[JsonPropertyName("category")]
	public string Category { get; set; } = string.Empty;
}

/// <summary>
/// Source-generated mapping context for <see cref="MappedDoc"/>.
/// Declares an index target named "mapping-test" with explicit shards, replicas, and refresh
/// interval so that the generated settings JSON can be verified against the live cluster.
/// </summary>
[ElasticsearchMappingContext]
[Entity<MappedDoc>(
	Target = EntityTarget.Index,
	Name = "mapping-test",
	Shards = 1,
	Replicas = 0,
	RefreshInterval = "1s")]
public static partial class MappingTestContext;

public class MappingIngestionTests(IngestionCluster cluster, ITestOutputHelper output)
	: IntegrationTestBase(cluster, output)
{
	// The template name is derived from entity Name="mapping-test" → "mapping-test-template"
	// The mappings component template is "{templateName}-mappings"
	private const string MappingsComponentName = "mapping-test-template-mappings";
	private const string IndexName = "mapping-test";

	[Fact]
	public async Task EnsureMappingsAreAppliedFromElasticMappingContext()
	{
		var typeContext = MappingTestContext.MappedDoc.Context;
		var slim = new CountdownEvent(1);
		var options = new IngestChannelOptions<MappedDoc>(Client.Transport, typeContext)
		{
			BufferOptions = new BufferOptions { WaitHandle = slim, OutboundBufferMaxSize = 1 }
		};
		var channel = new IngestChannel<MappedDoc>(options);

		var bootstrapped = await channel.BootstrapElasticsearchAsync(BootstrapMethod.Failure);
		bootstrapped.Should().BeTrue("Expected bootstrap to succeed with Elastic.Mapping context");

		// ── Verify the mappings component template was created with correct field types ──
		var componentResponse = await Client.Transport.RequestAsync<JsonResponse>(
			HttpMethod.GET, $"/_component_template/{MappingsComponentName}");

		componentResponse.ApiCallDetails.HttpStatusCode.Should().Be(200,
			"mappings component template should exist after bootstrap: {0}",
			componentResponse.ApiCallDetails.DebugInformation);

		var templatePath = "component_templates.[0].component_template.template";

		// [Keyword] on id → keyword field type
		componentResponse.Get<string>($"{templatePath}.mappings.properties.id.type")
			.Should().Be("keyword", "[Keyword] attribute on id should produce keyword type");

		// [Text] on title → text field type (source generator also adds a .keyword sub-field)
		componentResponse.Get<string>($"{templatePath}.mappings.properties.title.type")
			.Should().Be("text", "[Text] attribute on title should produce text type");

		// [Keyword] on category → keyword field type
		componentResponse.Get<string>($"{templatePath}.mappings.properties.category.type")
			.Should().Be("keyword", "[Keyword] attribute on category should produce keyword type");

		// ── Verify entity-level settings are embedded in the mappings component template ──
		// Source generator emits: {"number_of_shards":1,"number_of_replicas":0,"refresh_interval":"1s"}
		// Elasticsearch normalises numeric settings to strings on GET
		componentResponse.Get<string>($"{templatePath}.settings.number_of_shards")
			.Should().Be("1", "Shards = 1 on [Entity<>] should embed number_of_shards in the component template");

		componentResponse.Get<string>($"{templatePath}.settings.number_of_replicas")
			.Should().Be("0", "Replicas = 0 on [Entity<>] should embed number_of_replicas in the component template");

		componentResponse.Get<string>($"{templatePath}.settings.refresh_interval")
			.Should().Be("1s", "RefreshInterval = \"1s\" on [Entity<>] should embed refresh_interval in the component template");

		// ── Write a document and verify it is indexed under the mapped index ──────────
		channel.TryWrite(new MappedDoc { Id = "mapping-doc-1", Title = "Integration test document", Category = "tests" });
		if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
			throw new Exception($"Document was not persisted within 10 seconds: {channel}");

		var refreshResponse = await Client.Transport.RequestAsync<JsonResponse>(
			HttpMethod.POST, $"/{IndexName}/_refresh");
		refreshResponse.ApiCallDetails.HttpStatusCode.Should().Be(200,
			"refresh should succeed: {0}", refreshResponse.ApiCallDetails.DebugInformation);

		var searchResponse = await Client.Transport.RequestAsync<JsonResponse>(
			HttpMethod.GET, $"/{IndexName}/_search");
		searchResponse.Get<long>("hits.total.value").Should().Be(1);
		searchResponse.Get<string>("hits.hits.[0]._source.id").Should().Be("mapping-doc-1");
		searchResponse.Get<string>("hits.hits.[0]._source.title").Should().Be("Integration test document");
		searchResponse.Get<string>("hits.hits.[0]._source.category").Should().Be("tests");

		// ── Verify the live index actually picked up the settings from the component template ──
		var settingsResponse = await Client.Transport.RequestAsync<JsonResponse>(
			HttpMethod.GET, $"/{IndexName}/_settings");
		settingsResponse.Get<string>($"{IndexName}.settings.index.number_of_shards")
			.Should().Be("1", "Shards = 1 on [Entity<>] should set number_of_shards on the live index");
		settingsResponse.Get<string>($"{IndexName}.settings.index.number_of_replicas")
			.Should().Be("0", "Replicas = 0 on [Entity<>] should set number_of_replicas on the live index");
	}
}
