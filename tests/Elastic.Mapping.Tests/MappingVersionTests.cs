// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping.Tests;

/// <summary>
/// Tests that the MappingVersion attribute property is correctly wired through the
/// source generator into <see cref="ElasticsearchTypeContext.MappingVersion"/>.
/// </summary>
public class MappingVersionTests
{
	[Test]
	public void IndexWithMappingVersion_FlowsToContext()
	{
		var ctx = VersionedMappingContext.SimpleDocument.Context;

		ctx.MappingVersion.Should().Be("3.2.1");
	}

	[Test]
	public void DataStreamWithMappingVersion_FlowsToContext()
	{
		var ctx = VersionedMappingContext.SimpleDocumentVersioned.Context;

		ctx.MappingVersion.Should().Be("1.0.0");
	}

	[Test]
	public void IndexWithoutMappingVersion_IsNull()
	{
		var ctx = VersionedMappingContext.SimpleDocumentUnversioned.Context;

		ctx.MappingVersion.Should().BeNull(
			"MappingVersion was not set on the attribute, so it defaults to null (hash-only)");
	}

	[Test]
	public void VersionedAndUnversioned_HaveSameMappedType()
	{
		var versioned = VersionedMappingContext.SimpleDocument.Context;
		var unversioned = VersionedMappingContext.SimpleDocumentUnversioned.Context;

		versioned.MappedType.Should().Be<SimpleDocument>();
		unversioned.MappedType.Should().Be<SimpleDocument>();
	}

	[Test]
	public void VersionedIndex_HasExpectedEntityTarget()
	{
		var ctx = VersionedMappingContext.SimpleDocument.Context;

		ctx.EntityTarget.Should().Be(EntityTarget.Index);
		ctx.IndexStrategy!.WriteTarget.Should().Be("versioned-index");
	}

	[Test]
	public void VersionedDataStream_HasExpectedEntityTarget()
	{
		var ctx = VersionedMappingContext.SimpleDocumentVersioned.Context;

		ctx.EntityTarget.Should().Be(EntityTarget.DataStream);
		ctx.IndexStrategy!.Type.Should().Be("logs");
		ctx.IndexStrategy!.Dataset.Should().Be("versioned");
	}
}
