// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading;
using System.Threading.Tasks;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// No-op alias strategy. Used for DataStream, Index, and WiredStream targets.
/// </summary>
public class NoAliasStrategy : IAliasStrategy
{
	/// <inheritdoc />
	public Task<bool> ApplyAliasesAsync(AliasContext context, CancellationToken ctx = default) =>
		Task.FromResult(true);

	/// <inheritdoc />
	public bool ApplyAliases(AliasContext context) => true;
}
