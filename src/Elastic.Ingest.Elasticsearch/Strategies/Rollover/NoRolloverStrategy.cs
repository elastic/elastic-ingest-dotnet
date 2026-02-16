// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading;
using System.Threading.Tasks;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// No-op rollover strategy. Always returns true without performing any action.
/// </summary>
public class NoRolloverStrategy : IRolloverStrategy
{
	/// <inheritdoc />
	public Task<bool> RolloverAsync(RolloverContext context, CancellationToken ctx = default) =>
		Task.FromResult(true);

	/// <inheritdoc />
	public bool Rollover(RolloverContext context) => true;
}
