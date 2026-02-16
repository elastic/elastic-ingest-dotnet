// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using Elastic.Mapping;

namespace Elastic.Ingest.Elasticsearch.Queries;

/// <summary>
/// Configuration for <see cref="DeleteByQuery"/>.
/// </summary>
public class DeleteByQueryOptions
{
	/// <summary> The target index. When null, resolved from <see cref="TypeContext"/> (WriteAlias). </summary>
	public string? Index { get; init; }

	/// <summary> Optional type context for automatic index resolution. Uses WriteAlias. </summary>
	public ElasticsearchTypeContext? TypeContext { get; init; }

	/// <summary> JSON query body. </summary>
	public required string QueryBody { get; init; }

	/// <summary> Throttle in requests per second. -1 for unlimited. </summary>
	public float? RequestsPerSecond { get; init; }

	/// <summary> Number of slices: "auto" or a number string. </summary>
	public string? Slices { get; init; }

	/// <summary> How often to poll the task status. Defaults to 5 seconds. </summary>
	public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);
}
