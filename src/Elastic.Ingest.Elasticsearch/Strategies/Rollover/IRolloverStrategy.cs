// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Context for rollover operations.
/// </summary>
public class RolloverContext
{
	/// <summary> The transport for Elasticsearch API calls. </summary>
	public required ITransport Transport { get; init; }

	/// <summary> The target alias or data stream name to rollover. </summary>
	public required string Target { get; init; }

	/// <summary> Optional maximum age condition (e.g. "7d"). </summary>
	public string? MaxAge { get; init; }

	/// <summary> Optional maximum primary shard size condition (e.g. "50gb"). </summary>
	public string? MaxSize { get; init; }

	/// <summary> Optional maximum document count condition. </summary>
	public long? MaxDocs { get; init; }
}

/// <summary>
/// Controls manual rollover of indices or data streams.
/// </summary>
public interface IRolloverStrategy
{
	/// <summary> Triggers a rollover asynchronously. Returns true if successful. </summary>
	Task<bool> RolloverAsync(RolloverContext context, CancellationToken ctx = default);

	/// <summary> Triggers a rollover synchronously. Returns true if successful. </summary>
	bool Rollover(RolloverContext context);
}
