// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using Elastic.Ingest.Elasticsearch.Helpers;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class PointInTimeSearchRequestBuilderTests
{
	[Test]
	public void OmitsSourceWhenIncludesAndExcludesUnset()
	{
		var options = new PointInTimeSearchOptions();
		var body = PointInTimeSearchRequestBuilder.BuildSearchBody("pit-1", options, searchAfter: null, sliceId: null, sliceMax: null);

		body.Should().Be(
			"""{"pit":{"id":"pit-1","keep_alive":"5m"},"size":1000,"sort":["_shard_doc"]}""");
		body.Should().NotContain("_source");
	}

	[Test]
	public void OmitsSourceWhenIncludesAndExcludesEmpty()
	{
		var options = new PointInTimeSearchOptions
		{
			SourceIncludes = Array.Empty<string>(),
			SourceExcludes = Array.Empty<string>()
		};
		var body = PointInTimeSearchRequestBuilder.BuildSearchBody("p", options, null, null, null);

		body.Should().NotContain("_source");
	}

	[Test]
	public void IncludesOnlySourceFilter()
	{
		var options = new PointInTimeSearchOptions
		{
			SourceIncludes = new[] { "url", "hash", "last_updated" }
		};
		var body = PointInTimeSearchRequestBuilder.BuildSearchBody("p", options, null, null, null);

		body.Should().EndWith(",\"_source\":{\"includes\":[\"url\",\"hash\",\"last_updated\"]}}");
	}

	[Test]
	public void ExcludesOnlySourceFilter()
	{
		var options = new PointInTimeSearchOptions
		{
			SourceExcludes = new[] { "large_blob" }
		};
		var body = PointInTimeSearchRequestBuilder.BuildSearchBody("p", options, null, null, null);

		body.Should().Contain(",\"_source\":{\"excludes\":[\"large_blob\"]}");
	}

	[Test]
	public void IncludesAndExcludesSourceFilter()
	{
		var options = new PointInTimeSearchOptions
		{
			SourceIncludes = new[] { "a", "b" },
			SourceExcludes = new[] { "c" }
		};
		var body = PointInTimeSearchRequestBuilder.BuildSearchBody("p", options, null, null, null);

		body.Should().Contain(",\"_source\":{\"includes\":[\"a\",\"b\"],\"excludes\":[\"c\"]}");
	}

	[Test]
	public void WithQuerySliceAndSourceProducesValidJson()
	{
		var options = new PointInTimeSearchOptions
		{
			QueryBody = """{"match_all":{}}""",
			SourceIncludes = new[] { "sku" }
		};
		var body = PointInTimeSearchRequestBuilder.BuildSearchBody("p", options, searchAfter: "[100]", sliceId: 1, sliceMax: 4);

		body.Should().Be(
			"""{"pit":{"id":"p","keep_alive":"5m"},"size":1000,"sort":["_shard_doc"],"query":{"match_all":{}},"search_after":[100],"slice":{"id":1,"max":4},"_source":{"includes":["sku"]}}""");
	}

	[Test]
	public void EscapesSpecialCharactersInFieldNames()
	{
		var options = new PointInTimeSearchOptions
		{
			SourceIncludes = new[] { """a"b""" }
		};
		var body = PointInTimeSearchRequestBuilder.BuildSearchBody("p", options, null, null, null);

		body.Should().Contain("\"includes\":[\"a\\\"b\"]");
	}
}
