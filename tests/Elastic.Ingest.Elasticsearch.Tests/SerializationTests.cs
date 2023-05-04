// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;
using Elastic.Ingest.Elasticsearch.Serialization;
using FluentAssertions;
using Xunit;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class SerializationTests
{
	[Fact]
	public void CanSerializeBulkResponseItem()
	{
		var json = "{\"index\":{\"status\":200}}";
		var item = JsonSerializer.Deserialize<BulkResponseItem>(json);

		item.Should().NotBeNull();

		var actual = JsonSerializer.Serialize(item);

		actual.Should().Be(json);
	}
}
