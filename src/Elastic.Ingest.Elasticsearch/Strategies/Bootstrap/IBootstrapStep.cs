// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading;
using System.Threading.Tasks;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// A single step in the bootstrap pipeline.
/// </summary>
public interface IBootstrapStep
{
	/// <summary> Descriptive name for logging/diagnostics. </summary>
	string Name { get; }

	/// <summary> Execute this step asynchronously. Returns false on failure. </summary>
	Task<bool> ExecuteAsync(BootstrapContext context, CancellationToken ctx = default);

	/// <summary> Execute this step synchronously. Returns false on failure. </summary>
	bool Execute(BootstrapContext context);
}
