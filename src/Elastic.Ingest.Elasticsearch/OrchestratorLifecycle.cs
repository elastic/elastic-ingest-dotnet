// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Threading;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>Where a sync orchestrator is in its life: it is started once, and a start that failed is final.</summary>
internal enum OrchestratorState
{
	NotStarted,
	Starting,
	Started,
	/// <summary>Starting threw. The channels it had created are gone, so there is nothing to write to or complete.</summary>
	Failed
}

/// <summary>
/// The start-up of a sync orchestrator as explicit states. It used to be implied by which channel fields were null, and a start that
/// failed halfway left some of them set: writes then hit a null secondary channel, and a second StartAsync leaked the first channels.
/// </summary>
internal sealed class OrchestratorLifecycle
{
	private int _state;

	public OrchestratorState State => (OrchestratorState)Volatile.Read(ref _state);

	/// <summary>NotStarted to Starting, once. A second start, or a start while one runs, throws.</summary>
	public void BeginStart()
	{
		if (Interlocked.CompareExchange(ref _state, (int)OrchestratorState.Starting, (int)OrchestratorState.NotStarted) != (int)OrchestratorState.NotStarted)
			throw new InvalidOperationException("StartAsync was already called on this orchestrator. Create a new orchestrator to start again.");
	}

	public void MarkStarted() => Volatile.Write(ref _state, (int)OrchestratorState.Started);

	public void MarkFailed() => Volatile.Write(ref _state, (int)OrchestratorState.Failed);

	public void EnsureStarted()
	{
		switch (State)
		{
			case OrchestratorState.Started:
				return;
			case OrchestratorState.Failed:
				throw new InvalidOperationException("StartAsync failed, so there is nothing to write to or complete. Create a new orchestrator.");
			default:
				throw new InvalidOperationException("Call StartAsync before writing or completing.");
		}
	}
}
