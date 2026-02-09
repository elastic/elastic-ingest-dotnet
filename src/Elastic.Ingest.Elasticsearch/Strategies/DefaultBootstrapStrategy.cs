// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Default bootstrap strategy that iterates an ordered list of steps.
/// </summary>
public class DefaultBootstrapStrategy : IBootstrapStrategy
{
	/// <summary>
	/// Creates a default bootstrap strategy with the given steps.
	/// </summary>
	public DefaultBootstrapStrategy(params IBootstrapStep[] steps) =>
		Steps = new List<IBootstrapStep>(steps);

	/// <summary>
	/// Creates a default bootstrap strategy with the given steps.
	/// </summary>
	public DefaultBootstrapStrategy(IReadOnlyList<IBootstrapStep> steps) =>
		Steps = steps;

	/// <inheritdoc />
	public IReadOnlyList<IBootstrapStep> Steps { get; }

	/// <inheritdoc />
	public async Task<bool> BootstrapAsync(BootstrapContext context, CancellationToken ctx = default)
	{
		foreach (var step in Steps)
		{
			if (!await step.ExecuteAsync(context, ctx).ConfigureAwait(false))
				return false;
		}
		return true;
	}

	/// <inheritdoc />
	public bool Bootstrap(BootstrapContext context)
	{
		foreach (var step in Steps)
		{
			if (!step.Execute(context))
				return false;
		}
		return true;
	}
}
