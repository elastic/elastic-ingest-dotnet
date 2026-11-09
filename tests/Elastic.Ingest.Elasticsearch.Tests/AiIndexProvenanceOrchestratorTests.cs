// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#nullable enable

using System;
using System.Threading.Tasks;
using Elastic.Mapping;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class ProvenanceDocument : AiIndexDocument;

public class AiIndexProvenanceOrchestratorTests
{
	private static ITransport Transport => TestSetup.SharedTransport;

	private static readonly AiIndexActor Actor = new()
	{
		Uri = "workflow://docs-sync",
		Metadata = new() { ["version"] = "3", ["run_id"] = "r1" }
	};

	private static ElasticsearchTypeContext Context(string writeTarget, Type mappedType, Func<object, string?> getId) =>
		new(
			() => "{}",
			() => "{}",
			() => "{}",
			"test-hash",
			"settings-hash",
			"mappings-hash",
			new IndexStrategy { WriteTarget = writeTarget },
			new SearchStrategy(),
			EntityTarget.Index,
			GetId: getId,
			GetTimestamp: static _ => DateTimeOffset.UtcNow,
			MappedType: mappedType,
			SetBatchIndexDate: static (_, _) => { },
			SetLastUpdated: static (_, _) => { },
			BatchIndexDateFieldName: "batch_index_date",
			LastUpdatedFieldName: "last_updated"
		);

	private static ElasticsearchTypeContext ProvenanceContext(string writeTarget) =>
		Context(writeTarget, typeof(ProvenanceDocument), static obj => ((ProvenanceDocument)obj).Id);

	private static ElasticsearchTypeContext PlainContext(string writeTarget) =>
		Context(writeTarget, typeof(TestDocument), static obj => ((TestDocument)obj).Timestamp.ToString("o"));

	private static IncrementalSyncOrchestrator<ProvenanceDocument> Incremental(AiIndexActor? provenance) =>
		new(Transport, ProvenanceContext("prov-incr-primary"), ProvenanceContext("prov-incr-secondary")) { Provenance = provenance };

	private static DeltaSyncOrchestrator<ProvenanceDocument> Delta(AiIndexActor? provenance) =>
		new(Transport, ProvenanceContext("prov-delta-primary"), ProvenanceContext("prov-delta-secondary")) { Provenance = provenance };

	[Test]
	public async Task IncrementalSyncStampsEveryDocumentItWritesWithTheBatchTimestamp()
	{
		using var orchestrator = Incremental(Actor);
		await orchestrator.StartAsync(BootstrapMethod.Silent);
		var first = new ProvenanceDocument { Id = "1" };
		var second = new ProvenanceDocument { Id = "2" };

		orchestrator.TryWrite(first);
		orchestrator.TryWrite(second);

		first.Governance!.Provenance!.UpdatedBy.Should().BeSameAs(Actor);
		first.Governance.Provenance.CreatedBy.Should().BeSameAs(Actor);
		first.UpdatedAt.Should().NotBeNull().And.Be(second.UpdatedAt);
		second.Governance!.Provenance!.UpdatedBy.Should().BeSameAs(Actor);
	}

	[Test]
	public async Task IncrementalSyncStampsDocumentsWrittenWithWaitToWriteAsync()
	{
		using var orchestrator = Incremental(Actor);
		await orchestrator.StartAsync(BootstrapMethod.Silent);
		var document = new ProvenanceDocument { Id = "1" };

		await orchestrator.WaitToWriteAsync(document);

		document.Governance!.Provenance!.UpdatedBy.Should().BeSameAs(Actor);
	}

	[Test]
	public async Task IncrementalSyncKeepsTheCreatorOfADocumentThatHasOne()
	{
		using var orchestrator = Incremental(Actor);
		await orchestrator.StartAsync(BootstrapMethod.Silent);
		var original = new AiIndexActor { Uri = "workflow://first" };
		var document = new ProvenanceDocument { Id = "1", Governance = new() { Provenance = new() { CreatedBy = original } } };

		orchestrator.TryWrite(document);

		document.Governance!.Provenance!.CreatedBy.Should().BeSameAs(original);
		document.Governance.Provenance.UpdatedBy.Should().BeSameAs(Actor);
	}

	[Test]
	public async Task IncrementalSyncWithoutProvenanceLeavesDocumentsAlone()
	{
		using var orchestrator = Incremental(provenance: null);
		await orchestrator.StartAsync(BootstrapMethod.Silent);
		var document = new ProvenanceDocument { Id = "1" };

		orchestrator.TryWrite(document);

		document.Governance.Should().BeNull();
		document.UpdatedAt.Should().BeNull();
	}

	[Test]
	public async Task IncrementalSyncIgnoresDocumentsThatAreNotAiIndexDocuments()
	{
		using var orchestrator = new IncrementalSyncOrchestrator<TestDocument>(
			Transport, PlainContext("prov-plain-primary"), PlainContext("prov-plain-secondary")) { Provenance = Actor };
		await orchestrator.StartAsync(BootstrapMethod.Silent);

		var written = orchestrator.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });

		written.Should().BeTrue();
	}

	[Test]
	public async Task DeltaSyncStampsEveryDocumentItWrites()
	{
		using var orchestrator = Delta(Actor);
		await orchestrator.StartAsync(BootstrapMethod.Silent);
		var document = new ProvenanceDocument { Id = "1" };

		orchestrator.TryWrite(document);

		document.Governance!.Provenance!.UpdatedBy.Should().BeSameAs(Actor);
		document.Governance.Provenance.CreatedBy.Should().BeSameAs(Actor);
		document.UpdatedAt.Should().NotBeNull();
	}

	[Test]
	public async Task DeltaSyncWithoutProvenanceLeavesDocumentsAlone()
	{
		using var orchestrator = Delta(provenance: null);
		await orchestrator.StartAsync(BootstrapMethod.Silent);
		var document = new ProvenanceDocument { Id = "1" };

		orchestrator.TryWrite(document);

		document.Governance.Should().BeNull();
	}
}
