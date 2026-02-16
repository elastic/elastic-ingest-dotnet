// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Orchestrates an ordered list of bootstrap steps.
/// </summary>
public interface IBootstrapStrategy
{
	/// <summary> The ordered list of steps to execute. </summary>
	IReadOnlyList<IBootstrapStep> Steps { get; }

	/// <summary> Execute all steps asynchronously. Returns false if any step fails. </summary>
	Task<bool> BootstrapAsync(BootstrapContext context, CancellationToken ctx = default);

	/// <summary> Execute all steps synchronously. Returns false if any step fails. </summary>
	bool Bootstrap(BootstrapContext context);
}
