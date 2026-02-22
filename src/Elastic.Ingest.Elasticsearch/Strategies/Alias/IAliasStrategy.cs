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

	/// <summary>
	/// The concrete index name to apply aliases to (e.g., <c>my-index-2026.02.22.143055</c>).
	/// <para>
	/// May be empty when the caller does not track the timestamped index name directly.
	/// For example, <see cref="IncrementalSyncOrchestrator{TEvent}"/> precomputes this from
	/// <see cref="IngestChannel{TEvent}.IndexName"/> from its strategy, but callers
	/// using <see cref="IngestChannel{TEvent}.ApplyAliasesAsync"/> directly may not know the
	/// concrete index. When empty, <see cref="LatestAndSearchAliasStrategy"/> falls back to
	/// resolving the latest index via the <c>_resolve/index</c> API.
	/// </para>
	/// </summary>
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
