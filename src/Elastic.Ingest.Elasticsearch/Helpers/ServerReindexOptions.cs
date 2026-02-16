// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using Elastic.Mapping;

namespace Elastic.Ingest.Elasticsearch.Helpers;

/// <summary>
/// Configuration for <see cref="ServerReindex"/>.
/// </summary>
public class ServerReindexOptions
{
	/// <summary> Source index name. When null, resolved from <see cref="SourceContext"/> (WriteAlias). </summary>
	public string? Source { get; init; }

	/// <summary> Destination index name. When null, resolved from <see cref="DestinationContext"/> (WriteAlias). </summary>
	public string? Destination { get; init; }

	/// <summary> Optional type context for automatic source index resolution. Uses WriteAlias. </summary>
	public ElasticsearchTypeContext? SourceContext { get; init; }

	/// <summary> Optional type context for automatic destination index resolution. Uses WriteAlias. </summary>
	public ElasticsearchTypeContext? DestinationContext { get; init; }

	/// <summary> Optional JSON query body to filter source documents. </summary>
	public string? Query { get; init; }

	/// <summary> Optional ingest pipeline name. </summary>
	public string? Pipeline { get; init; }

	/// <summary> Throttle in requests per second. -1 for unlimited. </summary>
	public float? RequestsPerSecond { get; init; }

	/// <summary> Number of slices: "auto" or a number string. </summary>
	public string? Slices { get; init; }

	/// <summary> How often to poll the task status. Defaults to 5 seconds. </summary>
	public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

	/// <summary> Optional full override body JSON. When set, Source/Destination/Query/Pipeline are ignored. </summary>
	public string? Body { get; init; }
}
