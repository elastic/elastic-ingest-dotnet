// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Channels;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// Common context returned by <see cref="ISyncOrchestrator{TEvent}.StartAsync"/>.
/// Concrete orchestrators return a richer subtype; callers using the interface
/// receive this view.
/// </summary>
public interface ISyncOrchestratorContext
{
	/// <summary> The resolved ingest strategy (Reindex or Multiplex). </summary>
	IngestSyncStrategy Strategy { get; }

	/// <summary> The batch timestamp stamped on every document in this run. </summary>
	DateTimeOffset BatchTimestamp { get; }

	/// <summary> The resolved primary write alias. </summary>
	string PrimaryWriteAlias { get; }

	/// <summary> The resolved secondary write alias. </summary>
	string SecondaryWriteAlias { get; }

	/// <summary> The resolved primary read target (read alias or fallback to write alias). </summary>
	string PrimaryReadAlias { get; }

	/// <summary> The resolved secondary read target (read alias or fallback to write alias). </summary>
	string SecondaryReadAlias { get; }

	/// <summary> Rollover decision details for the primary index. </summary>
	IndexRolloverInfo? PrimaryRollover { get; }

	/// <summary> Rollover decision details for the secondary index. </summary>
	IndexRolloverInfo? SecondaryRollover { get; }
}

/// <summary>
/// Shared lifecycle interface for orchestrators that synchronise a document type
/// across a primary and secondary Elasticsearch index pair.
/// <para>
/// Callers that need to work with either <see cref="IncrementalSyncOrchestrator{TEvent}"/>
/// or <see cref="DeltaSyncOrchestrator{TEvent}"/> interchangeably should target this
/// interface. Orchestrator-specific setup (e.g.
/// <c>DeltaSyncOrchestrator.BackfillRolledOverIndicesAsync</c>) is performed against
/// the concrete type before handing off to code that targets this interface.
/// </para>
/// </summary>
public interface ISyncOrchestrator<TEvent> : IBufferedChannel<TEvent>, IDisposable
	where TEvent : class
{
	/// <summary>
	/// Creates channels, runs bootstrap, and resolves the ingest strategy.
	/// Must be called before <see cref="IBufferedChannel{TEvent}.TryWrite"/> or
	/// <see cref="CompleteAsync"/>.
	/// </summary>
	Task<ISyncOrchestratorContext> StartAsync(BootstrapMethod method, CancellationToken ctx = default);

	/// <summary>
	/// Drains buffered writes, propagates changes to the secondary index, and applies
	/// alias updates. The exact completion steps differ per implementation:
	/// <see cref="IncrementalSyncOrchestrator{TEvent}"/> reaps stale documents;
	/// <see cref="DeltaSyncOrchestrator{TEvent}"/> does not.
	/// </summary>
	Task<bool> CompleteAsync(TimeSpan? drainMaxWait = null, CancellationToken ctx = default);

	/// <summary>
	/// Registers a task that runs before channel bootstrap (e.g. creating synonym sets).
	/// Returns <c>this</c> for chaining.
	/// </summary>
	ISyncOrchestrator<TEvent> AddPreBootstrapTask(Func<ITransport, CancellationToken, Task> task);

	/// <summary> The batch timestamp assigned when the orchestrator was created. </summary>
	DateTimeOffset BatchTimestamp { get; }

	/// <summary> The resolved ingest strategy after <see cref="StartAsync"/> completes. </summary>
	IngestSyncStrategy Strategy { get; }
}
