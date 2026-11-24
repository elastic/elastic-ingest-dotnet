// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Nodes;
using Elastic.Ingest.Elasticsearch.Strategies;
using FluentAssertions;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class MappingDiffTests
{
	/// <summary> The <c>GET {index}/_mapping</c> response for one index. </summary>
	private static string Live(string mappings, string index = "idx") =>
		"{\"" + index + "\":{\"mappings\":" + mappings + "}}";

	private static MappingDelta Diff(string desired, string live) => MappingDiff.ComputeAdditions(desired, live);

	private static void ShouldEqualJson(string actual, string expected)
	{
		actual.Should().NotBeNull();
		JsonNode.DeepEquals(JsonNode.Parse(actual), JsonNode.Parse(expected)).Should().BeTrue(
			$"expected {expected} but was {actual}");
	}

	[Test]
	public void IdenticalMappingsGiveAnEmptyDelta()
	{
		const string mappings = """{"properties":{"name":{"type":"keyword"},"tags":{"type":"keyword"}}}""";

		var delta = Diff(mappings, Live(mappings));

		delta.IsEmpty.Should().BeTrue();
		delta.Conflicts.Should().BeEmpty();
	}

	[Test]
	public void NewTopLevelFieldIsTheOnlyThingEmitted()
	{
		var delta = Diff(
			"""{"properties":{"reason":{"type":"keyword"},"reasons":{"type":"keyword"}}}""",
			Live("""{"properties":{"reason":{"type":"keyword"}}}"""));

		ShouldEqualJson(delta.Json, """{"properties":{"reasons":{"type":"keyword"}}}""");
	}

	[Test]
	public void IndexWithoutAnyPropertiesGetsEverything()
	{
		var delta = Diff(
			"""{"properties":{"a":{"type":"keyword"}}}""",
			Live("{}"));

		ShouldEqualJson(delta.Json, """{"properties":{"a":{"type":"keyword"}}}""");
	}

	[Test]
	public void NewChildOfAnExistingObjectIsWrappedInItsParent()
	{
		var delta = Diff(
			"""{"properties":{"author":{"properties":{"name":{"type":"keyword"},"email":{"type":"keyword"}}}}}""",
			Live("""{"properties":{"author":{"properties":{"name":{"type":"keyword"}}}}}"""));

		// plain objects carry no type in a mapping, so none is sent
		ShouldEqualJson(delta.Json, """{"properties":{"author":{"properties":{"email":{"type":"keyword"}}}}}""");
	}

	[Test]
	public void NewChildTwoLevelsDeepIsFound()
	{
		var delta = Diff(
			"""{"properties":{"a":{"properties":{"b":{"properties":{"c":{"type":"keyword"},"d":{"type":"long"}}}}}}}""",
			Live("""{"properties":{"a":{"properties":{"b":{"properties":{"c":{"type":"keyword"}}}}}}}"""));

		ShouldEqualJson(delta.Json, """{"properties":{"a":{"properties":{"b":{"properties":{"d":{"type":"long"}}}}}}}""");
	}

	[Test]
	public void NewChildOfANestedFieldKeepsTheNestedType()
	{
		var delta = Diff(
			"""{"properties":{"lines":{"type":"nested","properties":{"sku":{"type":"keyword"},"qty":{"type":"long"}}}}}""",
			Live("""{"properties":{"lines":{"type":"nested","properties":{"sku":{"type":"keyword"}}}}}"""));

		ShouldEqualJson(delta.Json, """{"properties":{"lines":{"type":"nested","properties":{"qty":{"type":"long"}}}}}""");
	}

	[Test]
	public void BrandNewNestedFieldIsEmittedWholeWithItsType()
	{
		var delta = Diff(
			"""{"properties":{"lines":{"type":"nested","properties":{"sku":{"type":"keyword"}}}}}""",
			Live("""{"properties":{"name":{"type":"keyword"}}}"""));

		ShouldEqualJson(delta.Json, """{"properties":{"lines":{"type":"nested","properties":{"sku":{"type":"keyword"}}}}}""");
	}

	[Test]
	public void ObjectVersusNestedIsAConflictAndChildrenAreNotPatched()
	{
		var delta = Diff(
			"""{"properties":{"lines":{"type":"nested","properties":{"sku":{"type":"keyword"},"qty":{"type":"long"}}}}}""",
			Live("""{"properties":{"lines":{"properties":{"sku":{"type":"keyword"}}}}}"""));

		delta.IsEmpty.Should().BeTrue();
		delta.Conflicts.Should().ContainSingle().Which.Should().StartWith("lines:");
	}

	[Test]
	public void NewMultiFieldIsEmittedWithTheParentType()
	{
		var delta = Diff(
			"""{"properties":{"title":{"type":"text","fields":{"keyword":{"type":"keyword"},"raw":{"type":"keyword"}}}}}""",
			Live("""{"properties":{"title":{"type":"text","fields":{"keyword":{"type":"keyword"}}}}}"""));

		ShouldEqualJson(delta.Json, """{"properties":{"title":{"type":"text","fields":{"raw":{"type":"keyword"}}}}}""");
	}

	[Test]
	public void ChangedTypeIsReportedAndNotEmitted()
	{
		var delta = Diff(
			"""{"properties":{"reason":{"type":"keyword"},"extra":{"type":"long"}}}""",
			Live("""{"properties":{"reason":{"type":"text"}}}"""));

		ShouldEqualJson(delta.Json, """{"properties":{"extra":{"type":"long"}}}""");
		delta.Conflicts.Should().ContainSingle().Which.Should().StartWith("reason:").And.Contain("text").And.Contain("keyword");
	}

	[Test]
	public void ConflictOnANestedChildIsReportedWithItsPath()
	{
		var delta = Diff(
			"""{"properties":{"author":{"properties":{"age":{"type":"keyword"}}}}}""",
			Live("""{"properties":{"author":{"properties":{"age":{"type":"long"}}}}}"""));

		delta.IsEmpty.Should().BeTrue();
		delta.Conflicts.Should().ContainSingle().Which.Should().StartWith("author.age:");
	}

	[Test]
	public void ExistingFieldWithChangedParametersIsLeftAlone()
	{
		var delta = Diff(
			"""{"properties":{"name":{"type":"keyword","ignore_above":512}}}""",
			Live("""{"properties":{"name":{"type":"keyword","ignore_above":256}}}"""));

		delta.IsEmpty.Should().BeTrue();
		delta.Conflicts.Should().BeEmpty();
	}

	[Test]
	public void FieldsRemovedFromTheMappingStayInTheIndexAndAreIgnored()
	{
		// e.g. a property that became [JsonIgnore]: the generated mapping no longer has it, the index still does
		var delta = Diff(
			"""{"properties":{"name":{"type":"keyword"}}}""",
			Live("""{"properties":{"name":{"type":"keyword"},"gone":{"type":"keyword"}}}"""));

		delta.IsEmpty.Should().BeTrue();
	}

	[Test]
	public void OnlyMissingRuntimeFieldsAreEmitted()
	{
		var delta = Diff(
			"""{"runtime":{"old":{"type":"keyword"},"new":{"type":"long","script":{"source":"emit(1)"}}}}""",
			Live("""{"runtime":{"old":{"type":"keyword"}}}"""));

		ShouldEqualJson(delta.Json, """{"runtime":{"new":{"type":"long","script":{"source":"emit(1)"}}}}""");
	}

	[Test]
	public void DynamicTemplatesAndOtherNonAdditiveSettingsAreIgnored()
	{
		var delta = Diff(
			"""{"dynamic":"strict","_source":{"enabled":true},"dynamic_templates":[{"s":{"match":"*","mapping":{"type":"keyword"}}}],"properties":{"a":{"type":"keyword"}}}""",
			Live("""{"properties":{"a":{"type":"keyword"}}}"""));

		delta.IsEmpty.Should().BeTrue();
	}

	[Test]
	public void FieldMustBePresentInEveryIndexBehindAnAlias()
	{
		const string response = """
			{"idx-1":{"mappings":{"properties":{"a":{"type":"keyword"},"b":{"type":"keyword"}}}},
			 "idx-2":{"mappings":{"properties":{"a":{"type":"keyword"}}}}}
			""";

		var delta = Diff("""{"properties":{"a":{"type":"keyword"},"b":{"type":"keyword"}}}""", response);

		ShouldEqualJson(delta.Json, """{"properties":{"b":{"type":"keyword"}}}""");
	}

	[Test]
	public void ResponseWithoutAnyIndexGivesAnEmptyDelta() =>
		Diff("""{"properties":{"a":{"type":"keyword"}}}""", "{}").IsEmpty.Should().BeTrue();

	[Test]
	public void FieldNamesNeedingEscapingSurvive()
	{
		var delta = Diff(
			"""{"properties":{"we\"ird":{"type":"keyword"}}}""",
			Live("{}"));

		ShouldEqualJson(delta.Json, """{"properties":{"we\"ird":{"type":"keyword"}}}""");
	}
}
