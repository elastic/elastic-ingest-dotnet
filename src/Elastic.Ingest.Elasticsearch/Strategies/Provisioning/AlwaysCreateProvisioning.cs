// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Provisioning strategy that always creates a new index. Default for DataStream and Index targets.
/// </summary>
public class AlwaysCreateProvisioning : IIndexProvisioningStrategy
{
	/// <inheritdoc />
	public Task<ProvisioningDecision> DecideAsync(BootstrapContext context, CancellationToken ctx = default) =>
		Task.FromResult(ProvisioningDecision.CreateNew);

	/// <inheritdoc />
	public ProvisioningDecision Decide(BootstrapContext context) =>
		ProvisioningDecision.CreateNew;

	/// <inheritdoc />
	public Task<string?> ResolveExistingIndexAsync(string indexPattern, string latestAlias, ITransport transport, CancellationToken ctx = default) =>
		Task.FromResult<string?>(null);

	/// <inheritdoc />
	public string? ResolveExistingIndex(string indexPattern, string latestAlias, ITransport transport) => null;
}
