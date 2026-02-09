// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Context for alias operations.
/// </summary>
public class AliasContext
{
	/// <summary> The transport for Elasticsearch API calls. </summary>
	public required ITransport Transport { get; init; }

	/// <summary> The index name to apply aliases to. </summary>
	public required string IndexName { get; init; }

	/// <summary> The index pattern matching all related indices. </summary>
	public required string IndexPattern { get; init; }

	/// <summary> The active search alias name. </summary>
	public string? ActiveSearchAlias { get; init; }
}

/// <summary>
/// Manages alias creation and swapping after indexing.
/// </summary>
public interface IAliasStrategy
{
	/// <summary> Apply aliases asynchronously. </summary>
	Task<bool> ApplyAliasesAsync(AliasContext context, CancellationToken ctx = default);

	/// <summary> Apply aliases synchronously. </summary>
	bool ApplyAliases(AliasContext context);
}
