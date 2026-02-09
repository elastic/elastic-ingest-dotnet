// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading;
using System.Threading.Tasks;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Provisioning decision: create a new index or reuse an existing one.
/// </summary>
public enum ProvisioningDecision
{
	/// <summary> Create a new index with the current timestamp. </summary>
	CreateNew,

	/// <summary> Reuse an existing index that matches the current hash. </summary>
	ReuseExisting
}

/// <summary>
/// Decides whether to create a new index or reuse an existing one (e.g. hash-based reuse).
/// </summary>
public interface IIndexProvisioningStrategy
{
	/// <summary> Decide asynchronously whether to create or reuse. </summary>
	Task<ProvisioningDecision> DecideAsync(BootstrapContext context, CancellationToken ctx = default);

	/// <summary> Decide synchronously whether to create or reuse. </summary>
	ProvisioningDecision Decide(BootstrapContext context);

	/// <summary> Resolve the name of an existing index to reuse, if any. </summary>
	Task<string?> ResolveExistingIndexAsync(string indexPattern, string latestAlias, ITransport transport, CancellationToken ctx = default);

	/// <summary> Resolve the name of an existing index to reuse, if any. </summary>
	string? ResolveExistingIndex(string indexPattern, string latestAlias, ITransport transport);
}
