// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using Elastic.Ingest.Elasticsearch.Serialization;

namespace Elastic.Ingest.Elasticsearch.Tests.Strategies;

public class IndexDocument
{
	private static int Counter;

	public DateTimeOffset Timestamp { get; set; }
	public int Id { get; } = ++Counter;
}
public class DataStreamDocument
{
	private static int Counter;

	public DateTimeOffset Timestamp { get; set; }
	public int Id { get; } = ++Counter;
}

public class TrackStrategy
{
	public int Id { get; init; }
	public HeaderSerializationStrategy Strategy { get; init; }
	public BulkHeader? Header { get; init; }

}
