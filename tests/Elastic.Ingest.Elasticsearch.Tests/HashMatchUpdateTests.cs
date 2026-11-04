// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#nullable enable

using System;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class HashMatchUpdateTests
{
	private static readonly DateTimeOffset Timestamp = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

	[Test]
	public void DefaultOnlyRefreshesTheBatchIndexDate()
	{
		var update = IncrementalSyncOrchestrator<TestDocument>.CreateHashMatchUpdate("hash", "combined", "batch_date", Timestamp, forceReindex: false);

		update.Field.Should().Be("hash");
		update.Hash.Should().Be("combined");
		update.UpdateScript.Should().Be("ctx._source.batch_date = params.batch_date");
		update.Parameters.Should().ContainKey("batch_date").WhoseValue.Should().Be(Timestamp.ToString("o"));
	}

	[Test]
	public void ForceReindexReplacesTheSourceAndKeepsTheStoredHash()
	{
		var update = IncrementalSyncOrchestrator<TestDocument>.CreateHashMatchUpdate("hash", "combined", "batch_date", Timestamp, forceReindex: true);

		update.Hash.Should().Be("combined", "the compared and stored hash stays deterministic");
		update.UpdateScript.Should().Be("ctx._source = params.doc; ctx._source.hash = params.hash");
	}
}
