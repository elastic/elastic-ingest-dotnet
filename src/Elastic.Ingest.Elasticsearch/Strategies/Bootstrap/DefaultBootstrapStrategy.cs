// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Default bootstrap strategy that iterates an ordered list of steps.
/// Validates step ordering constraints in the constructor.
/// </summary>
public class DefaultBootstrapStrategy : IBootstrapStrategy
{
	/// <summary>
	/// Creates a default bootstrap strategy with the given steps.
	/// Validates ordering constraints:
	/// <list type="bullet">
	/// <item><see cref="IlmPolicyStep"/> must precede <see cref="ComponentTemplateStep"/></item>
	/// <item><see cref="ComponentTemplateStep"/> must precede <see cref="IndexTemplateStep"/> / <see cref="DataStreamTemplateStep"/></item>
	/// <item><see cref="DataStreamLifecycleStep"/> must precede <see cref="DataStreamTemplateStep"/></item>
	/// </list>
	/// </summary>
	public DefaultBootstrapStrategy(params IBootstrapStep[] steps)
	{
		Steps = new List<IBootstrapStep>(steps);
		ValidateStepOrdering();
	}

	/// <summary>
	/// Creates a default bootstrap strategy with the given steps.
	/// </summary>
	public DefaultBootstrapStrategy(IReadOnlyList<IBootstrapStep> steps)
	{
		Steps = steps;
		ValidateStepOrdering();
	}

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

	private void ValidateStepOrdering()
	{
		var ilmIndex = IndexOf<IlmPolicyStep>();
		var componentIndex = IndexOf<ComponentTemplateStep>();
		var indexTemplateIndex = IndexOf<IndexTemplateStep>(); // also matches DataStreamTemplateStep
		var lifecycleIndex = IndexOf<DataStreamLifecycleStep>();
		var dataStreamTemplateIndex = Steps
			.Select((s, i) => (s, i))
			.Where(t => t.s.GetType() == typeof(DataStreamTemplateStep))
			.Select(t => (int?)t.i)
			.FirstOrDefault();

		if (ilmIndex.HasValue && componentIndex.HasValue && ilmIndex.Value > componentIndex.Value)
			throw new ArgumentException("IlmPolicyStep must precede ComponentTemplateStep.");

		if (componentIndex.HasValue && indexTemplateIndex.HasValue && componentIndex.Value > indexTemplateIndex.Value)
			throw new ArgumentException("ComponentTemplateStep must precede IndexTemplateStep/DataStreamTemplateStep.");

		if (lifecycleIndex.HasValue && dataStreamTemplateIndex.HasValue && lifecycleIndex.Value > dataStreamTemplateIndex.Value)
			throw new ArgumentException("DataStreamLifecycleStep must precede DataStreamTemplateStep.");
	}

	private int? IndexOf<T>() where T : IBootstrapStep =>
		Steps
			.Select((s, i) => (s, i))
			.Where(t => t.s is T)
			.Select(t => (int?)t.i)
			.FirstOrDefault();
}
