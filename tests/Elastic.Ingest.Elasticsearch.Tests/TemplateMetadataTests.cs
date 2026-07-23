// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Ingest.Elasticsearch.Strategies;
using FluentAssertions;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class TemplateMetadataReadMetaTests
{
	private const string Envelope = "{\"index_templates\":[{\"index_template\":{\"_meta\":{";

	private static string Meta(string inner) => Envelope + inner + "}}}]}";

	[Test]
	public void ParsesHashOnly()
	{
		var meta = TemplateMetadataHelper.ReadMeta(Meta("\"hash\":\"abc123\""));

		meta.Hash.Should().Be("abc123");
		meta.MappingVersion.Should().BeNull();
	}

	[Test]
	public void ParsesHashAndMappingVersion()
	{
		var meta = TemplateMetadataHelper.ReadMeta(
			Meta("\"hash\":\"abc123\",\"mapping_version\":\"2.0.0\""));

		meta.Hash.Should().Be("abc123");
		meta.MappingVersion.Should().Be("2.0.0");
	}

	[Test]
	public void ParsesMappingVersionOnly()
	{
		var meta = TemplateMetadataHelper.ReadMeta(Meta("\"mapping_version\":\"1.5.0\""));

		meta.Hash.Should().BeNull();
		meta.MappingVersion.Should().Be("1.5.0");
	}

	[Test]
	public void ParsesMappingVersionBeforeHash()
	{
		var meta = TemplateMetadataHelper.ReadMeta(
			Meta("\"mapping_version\":\"3.0.0\",\"hash\":\"def456\""));

		meta.Hash.Should().Be("def456");
		meta.MappingVersion.Should().Be("3.0.0");
	}

	[Test]
	public void ReturnsEmptyForEmptyString()
	{
		var meta = TemplateMetadataHelper.ReadMeta("");
		meta.Should().Be(TemplateMetadata.Empty);
	}

	[Test]
	public void ReturnsEmptyForMalformedJson()
	{
		var meta = TemplateMetadataHelper.ReadMeta("{not valid json at all}");
		meta.Should().Be(TemplateMetadata.Empty);
	}

	[Test]
	public void ReturnsEmptyForWrongPrefix()
	{
		var meta = TemplateMetadataHelper.ReadMeta("{\"something_else\":[]}");
		meta.Should().Be(TemplateMetadata.Empty);
	}

	[Test]
	public void ReturnsEmptyForEmptyMeta()
	{
		var meta = TemplateMetadataHelper.ReadMeta(Meta(""));

		meta.Hash.Should().BeNull();
		meta.MappingVersion.Should().BeNull();
	}

	[Test]
	public void HandlesEmptyHashValue()
	{
		var meta = TemplateMetadataHelper.ReadMeta(Meta("\"hash\":\"\""));

		meta.Hash.Should().Be("");
	}
}

public class TemplateMetadataShouldSkipBootstrapTests
{
	// ── Hash-only mode (localMappingVersion = null) ─────────────────────

	[Test]
	public void HashOnlyHashMatchesSkips()
	{
		var remote = new TemplateMetadata("abc123", null);
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "abc123", null)
			.Should().BeTrue("hash matches and no version guard is active");
	}

	[Test]
	public void HashOnlyHashDiffersProceeds()
	{
		var remote = new TemplateMetadata("abc123", null);
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "different", null)
			.Should().BeFalse("hash differs and no version guard is active");
	}

	[Test]
	public void HashOnlyRemoteHashNullProceeds()
	{
		var remote = new TemplateMetadata(null, null);
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "abc123", null)
			.Should().BeFalse("remote has no hash");
	}

	[Test]
	public void HashOnlyRemoteHashEmptyProceeds()
	{
		var remote = new TemplateMetadata("", null);
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "abc123", null)
			.Should().BeFalse("remote hash is empty");
	}

	[Test]
	public void HashOnlyRemoteIsEmptyProceeds()
	{
		TemplateMetadataHelper.ShouldSkipBootstrap(TemplateMetadata.Empty, "abc123", null)
			.Should().BeFalse("remote metadata is empty");
	}

	// ── Version guard (localMappingVersion is set) ──────────────────────

	[Test]
	public void VersionGuardRemoteNewerSkips()
	{
		var remote = new TemplateMetadata("different_hash", "2.0.0");
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "local_hash", "1.0.0")
			.Should().BeTrue("remote version 2.0.0 > local version 1.0.0, don't downgrade");
	}

	[Test]
	public void VersionGuardRemoteNewerPatchSkips()
	{
		var remote = new TemplateMetadata("different_hash", "1.0.1");
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "local_hash", "1.0.0")
			.Should().BeTrue("remote patch version 1.0.1 > local 1.0.0");
	}

	[Test]
	public void VersionGuardRemoteNewerMajorSkips()
	{
		var remote = new TemplateMetadata("different_hash", "3.0.0");
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "local_hash", "1.5.2")
			.Should().BeTrue("remote major version 3.0.0 > local 1.5.2");
	}

	[Test]
	public void VersionGuardSameVersionHashMatchesSkips()
	{
		var remote = new TemplateMetadata("abc123", "1.0.0");
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "abc123", "1.0.0")
			.Should().BeTrue("same version and hash matches");
	}

	[Test]
	public void VersionGuardSameVersionHashDiffersProceeds()
	{
		var remote = new TemplateMetadata("old_hash", "1.0.0");
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "new_hash", "1.0.0")
			.Should().BeFalse("same version but hash differs — templates changed");
	}

	[Test]
	public void VersionGuardLocalNewerHashDiffersProceeds()
	{
		var remote = new TemplateMetadata("old_hash", "1.0.0");
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "new_hash", "2.0.0")
			.Should().BeFalse("local is newer and hash differs — upgrade");
	}

	[Test]
	public void LocalNewerVersionHashMatchesStillSkips()
	{
		var remote = new TemplateMetadata("same_hash", "1.0.0");
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "same_hash", "2.0.0")
			.Should().BeTrue("hash still matches even though versions differ");
	}

	[Test]
	public void RemoteHasNoVersionFallsBackToHashAndMatches()
	{
		var remote = new TemplateMetadata("abc123", null);
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "abc123", "1.0.0")
			.Should().BeTrue("remote has no version, but hash matches");
	}

	[Test]
	public void RemoteHasNoVersionFallsBackToHashAndDiffers()
	{
		var remote = new TemplateMetadata("old_hash", null);
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "new_hash", "1.0.0")
			.Should().BeFalse("remote has no version and hash differs");
	}

	[Test]
	public void UnparseableRemoteVersionFallsBackToHash()
	{
		var remote = new TemplateMetadata("abc123", "not-a-version");
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "abc123", "1.0.0")
			.Should().BeTrue("unparseable remote version, falls back to hash which matches");
	}

	[Test]
	public void UnparseableLocalVersionFallsBackToHash()
	{
		var remote = new TemplateMetadata("different", "2.0.0");
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "different", "bad-version")
			.Should().BeTrue("unparseable local version, falls back to hash which matches");
	}

	[Test]
	public void BothVersionsUnparseableFallsBackToHash()
	{
		var remote = new TemplateMetadata("different_hash", "alpha");
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "local_hash", "beta")
			.Should().BeFalse("both versions unparseable, hashes differ");
	}

	[Test]
	public void ThreePartVsFourPartVersionComparesCorrectly()
	{
		var remote = new TemplateMetadata("different", "1.0.0.1");
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "local_hash", "1.0.0")
			.Should().BeTrue("1.0.0.1 > 1.0.0");
	}

	[Test]
	public void FourPartVersionEqualDoesNotSkipOnHashMismatch()
	{
		var remote = new TemplateMetadata("different", "1.0.0.0");
		TemplateMetadataHelper.ShouldSkipBootstrap(remote, "local_hash", "1.0.0.0")
			.Should().BeFalse("versions equal, hashes differ");
	}

	// ── Rolling deployment scenario ─────────────────────────────────────

	[Test]
	public void OlderPodDoesNotOverwriteNewerTemplates()
	{
		// Version N deployed and updated templates
		var remoteAfterVersionN = new TemplateMetadata("hash_from_v2", "2.0.0");

		// Version N-1 pod restarts, sees different hash
		var localHashFromV1 = "hash_from_v1";
		var localVersionV1 = "1.0.0";

		TemplateMetadataHelper.ShouldSkipBootstrap(remoteAfterVersionN, localHashFromV1, localVersionV1)
			.Should().BeTrue("N-1 pod must not overwrite N's templates");
	}

	[Test]
	public void NewerPodCanUpgradeTemplates()
	{
		// Version N-1 templates are on the cluster
		var remoteBeforeUpgrade = new TemplateMetadata("hash_from_v1", "1.0.0");

		// Version N pod deploys with updated mappings
		var localHashFromV2 = "hash_from_v2";
		var localVersionV2 = "2.0.0";

		TemplateMetadataHelper.ShouldSkipBootstrap(remoteBeforeUpgrade, localHashFromV2, localVersionV2)
			.Should().BeFalse("newer pod should upgrade the templates");
	}
}

public class TemplateMetadataBuildMappingVersionFragmentTests
{
	[Test]
	public void NullInputReturnsEmptyString()
	{
		TemplateMetadataHelper.BuildMappingVersionFragment(null)
			.Should().BeEmpty();
	}

	[Test]
	public void NonNullInputReturnsJsonFragment()
	{
		var fragment = TemplateMetadataHelper.BuildMappingVersionFragment("1.2.3");

		fragment.Should().Contain("\"mapping_version\"");
		fragment.Should().Contain("\"1.2.3\"");
		fragment.Should().StartWith(",", "fragment is appended after existing _meta fields");
	}

	[Test]
	public void DifferentVersionsProduceDifferentFragments()
	{
		var f1 = TemplateMetadataHelper.BuildMappingVersionFragment("1.0.0");
		var f2 = TemplateMetadataHelper.BuildMappingVersionFragment("2.0.0");

		f1.Should().NotBe(f2);
	}
}
