// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Elastic.Mapping.Tests;

public class AiIndexProvenanceTests
{
	private static readonly DateTimeOffset Written = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

	private static AiIndexActor Workflow(string run = "r1") =>
		new() { Uri = "workflow://docs-sync", Metadata = new() { ["version"] = "3", ["run_id"] = run } };

	[Test]
	public void StampProvenanceRecordsTheActorAsCreatorAndUpdaterOfANewDocument()
	{
		var actor = Workflow();
		var document = new AiArticle();

		document.StampProvenance(actor, Written);

		document.Governance!.Provenance!.CreatedBy.Should().BeSameAs(actor);
		document.Governance.Provenance.UpdatedBy.Should().BeSameAs(actor);
		document.UpdatedAt.Should().Be(Written);
	}

	[Test]
	public void StampProvenanceKeepsAnExistingCreator()
	{
		var original = new AiIndexActor { Uri = "workflow://first" };
		var document = new AiArticle
		{
			Governance = new() { Provenance = new() { CreatedBy = original } }
		};

		document.StampProvenance(Workflow(), Written);

		document.Governance!.Provenance!.CreatedBy.Should().BeSameAs(original);
		document.Governance.Provenance.UpdatedBy!.Uri.Should().Be("workflow://docs-sync");
	}

	[Test]
	public void StampProvenanceReplacesAnExistingUpdater()
	{
		var document = new AiArticle();
		document.StampProvenance(Workflow("r1"), Written);
		var later = Workflow("r2");

		document.StampProvenance(later, Written.AddHours(1));

		document.Governance!.Provenance!.UpdatedBy.Should().BeSameAs(later);
		document.UpdatedAt.Should().Be(Written.AddHours(1));
		document.Governance.Provenance.CreatedBy!.Metadata!["run_id"].Should().Be("r1");
	}

	[Test]
	public void StampProvenanceLeavesTheLifecycleAlone()
	{
		var document = new AiArticle { Governance = new() { Lifecycle = new() { Status = "deleted" } } };

		document.StampProvenance(Workflow(), Written);

		document.Governance!.Lifecycle!.Status.Should().Be("deleted");
	}

	[Test]
	public void StampedProvenanceSerializesWithTheNamesOfTheBaseMapping()
	{
		var document = new AiArticle();
		document.StampProvenance(Workflow(), Written);

		var json = JsonNode.Parse(JsonSerializer.Serialize(document))!;

		json["updated_at"]!.GetValue<DateTimeOffset>().Should().Be(Written);
		var updatedBy = json["governance"]!["provenance"]!["updated_by"]!;
		updatedBy["uri"]!.GetValue<string>().Should().Be("workflow://docs-sync");
		updatedBy["metadata"]!["run_id"]!.GetValue<string>().Should().Be("r1");
		json["governance"]!["provenance"]!["created_by"]!["uri"]!.GetValue<string>().Should().Be("workflow://docs-sync");
		json["governance"]!.AsObject().ContainsKey("lifecycle").Should().BeFalse();
	}
}
