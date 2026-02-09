// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading;
using System.Threading.Tasks;

namespace Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;

/// <summary>
/// A no-op bootstrap step. Used for WiredStreams where bootstrap is managed by Elasticsearch.
/// </summary>
public class NoopBootstrapStep : IBootstrapStep
{
	/// <inheritdoc />
	public string Name => "Noop";

	/// <inheritdoc />
	public Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken ctx = default) =>
		Task.FromResult(true);

	/// <inheritdoc />
	public bool Execute(BootstrapContext context) => true;
}
