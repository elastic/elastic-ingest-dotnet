// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Elastic.Mapping;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class DeltaSyncOrchestratorTests
{
	private static ITransport Transport => TestSetup.SharedTransport;

	// ── Type context helpers ─────────────────────────────────────────────

	private static ElasticsearchTypeContext CreateTypeContext(
		string writeTarget,
		string readAlias,
		string searchPattern,
		string datePattern = "yyyy.MM.dd.HHmmss"
	) =>
		new(
			() => "{}",
			() => "{}",
			() => "{}",
			"test-hash",
			"settings-hash",
			"mappings-hash",
			new IndexStrategy { WriteTarget = writeTarget, DatePattern = datePattern },
			new SearchStrategy { ReadAlias = readAlias, Pattern = searchPattern },
			EntityTarget.Index,
			GetId: static obj => ((TestDocument)obj).Timestamp.ToString("o"),
			GetTimestamp: static obj => ((TestDocument)obj).Timestamp,
			MappedType: typeof(TestDocument)
		);

	// A context WITH the required batch setters
	private static ElasticsearchTypeContext CreateBatchContext(string writeTarget) =>
		new(
			() => "{}",
			() => "{}",
			() => "{}",
			"test-hash",
			"settings-hash",
			"mappings-hash",
			new IndexStrategy { WriteTarget = writeTarget },
			new SearchStrategy(),
			EntityTarget.Index,
			GetId: static obj => ((TestDocument)obj).Timestamp.ToString("o"),
			GetTimestamp: static obj => ((TestDocument)obj).Timestamp,
			MappedType: typeof(TestDocument),
			SetBatchIndexDate: static (_, _) => { },
			SetLastUpdated: static (_, _) => { },
			BatchIndexDateFieldName: "batch_index_date",
			LastUpdatedFieldName: "last_updated"
		);

	private static DeltaSyncOrchestrator<TestDocument> CreateOrchestrator(ITransport transport) =>
		new(transport, CreateBatchContext("delta-primary"), CreateBatchContext("delta-secondary"));

	// Returns a transport where _cat/aliases echoes back a fixed "previous" index name,
	// causing StartAsync to detect a rollover and queue a backfill task.
	private static ITransport CreateRolloverTransport(string previousIndex = "delta-primary-2024.01.01.000000") =>
		TestSetup.CreateClient(v => v
			.ClientCalls(r => r.OnPath("_cat").SucceedAlways()
				.ReturnByteResponse(Encoding.UTF8.GetBytes(previousIndex), "text/plain"))
			.ClientCalls(r => r.OnPath("_bulk").SucceedAlways()
				.ReturnResponse(new { items = new[] { new { create = new { status = 201 } } } }))
			.ClientCalls(r => r.SucceedAlways().ReturnResponse(new { }))
		);

	// ── Constructor validation ───────────────────────────────────────────

	[Test]
	public void ConstructorThrowsWhenBatchIndexDateMissing()
	{
		var primary = CreateTypeContext("delta-primary", "delta-primary-search", "delta-primary-*");
		var secondary = CreateTypeContext("delta-secondary", "delta-secondary-search", "delta-secondary-*");

		var act = () => new DeltaSyncOrchestrator<TestDocument>(Transport, primary, secondary);
		act.Should().Throw<ArgumentException>()
			.WithMessage("*[BatchIndexDate]*")
			.And.ParamName.Should().Be("primary");
	}

	[Test]
	public void ConstructorThrowsWhenLastUpdatedMissing()
	{
		var primary = CreateTypeContext("delta-primary", "delta-primary-search", "delta-primary-*") with
		{
			SetBatchIndexDate = static (_, _) => { }
		};
		var secondary = CreateTypeContext("delta-secondary", "delta-secondary-search", "delta-secondary-*");

		var act = () => new DeltaSyncOrchestrator<TestDocument>(Transport, primary, secondary);
		act.Should().Throw<ArgumentException>()
			.WithMessage("*[LastUpdated]*")
			.And.ParamName.Should().Be("primary");
	}

	[Test]
	public void ConstructorSucceedsWhenBatchFieldsPresent()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		orchestrator.Strategy.Should().Be(IngestSyncStrategy.Multiplex);
		orchestrator.LastUpdatedField.Should().Be("last_updated");
		orchestrator.BatchIndexDateField.Should().Be("batch_index_date");
		orchestrator.BatchTimestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
	}

	// ── TryWrite guards ──────────────────────────────────────────────────

	[Test]
	public void TryWriteBeforeStartThrows()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		var act = () => orchestrator.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		act.Should().Throw<InvalidOperationException>().WithMessage("*StartAsync*");
	}

	[Test]
	public async Task TryWriteThrowsWhenPendingRolloverBackfillsNonempty()
	{
		using var orchestrator = new DeltaSyncOrchestrator<TestDocument>(
			CreateRolloverTransport(),
			CreateBatchContext("delta-primary"),
			CreateBatchContext("delta-secondary"));

		await orchestrator.StartAsync(BootstrapMethod.Silent);
		// PendingRolloverBackfills is on the concrete context — verify indirectly via the guard
		var act = () => orchestrator.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*BackfillRolledOverIndicesAsync*");
	}

	[Test]
	public async Task WaitToWriteAsyncThrowsWhenPendingRolloverBackfillsNonempty()
	{
		using var orchestrator = new DeltaSyncOrchestrator<TestDocument>(
			CreateRolloverTransport(),
			CreateBatchContext("delta-primary"),
			CreateBatchContext("delta-secondary"));

		await orchestrator.StartAsync(BootstrapMethod.Silent);

		var act = async () => await orchestrator.WaitToWriteAsync(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("*BackfillRolledOverIndicesAsync*");
	}

	// ── StartAsync ───────────────────────────────────────────────────────

	[Test]
	public async Task StartSelectsMultiplexWhenBothIndicesFresh()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		var context = await orchestrator.StartAsync(BootstrapMethod.Silent);
		context.Strategy.Should().Be(IngestSyncStrategy.Multiplex);
		orchestrator.Strategy.Should().Be(IngestSyncStrategy.Multiplex);
	}

	[Test]
	public async Task StartPopulatesRolloverInfoOnContext()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		var context = await orchestrator.StartAsync(BootstrapMethod.Silent);

		context.PrimaryRollover.Should().NotBeNull();
		context.PrimaryRollover!.Label.Should().Be("primary");
		context.SecondaryRollover.Should().NotBeNull();
		context.SecondaryRollover!.Label.Should().Be("secondary");
	}

	[Test]
	public async Task StartExecutesPreBootstrapTasks()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		var executed = false;
		orchestrator.AddPreBootstrapTask(async (_, _) => { executed = true; await Task.CompletedTask; });

		await orchestrator.StartAsync(BootstrapMethod.Silent);
		executed.Should().BeTrue();
	}

	[Test]
	public async Task OnRolloverDecisionInvokedForBothIndices()
	{
		var decisions = new List<IndexRolloverInfo>();
		using var orchestrator = new DeltaSyncOrchestrator<TestDocument>(
			Transport,
			CreateBatchContext("delta-primary"),
			CreateBatchContext("delta-secondary"))
		{
			OnRolloverDecision = info => decisions.Add(info)
		};

		await orchestrator.StartAsync(BootstrapMethod.Silent);

		decisions.Should().HaveCount(2);
		decisions[0].Label.Should().Be("primary");
		decisions[1].Label.Should().Be("secondary");
	}

	// ── BackfillRolledOverIndicesAsync ───────────────────────────────────

	[Test]
	public async Task BackfillYieldsNothingWhenNoRollover()
	{
		using var orchestrator = CreateOrchestrator(Transport);
		await orchestrator.StartAsync(BootstrapMethod.Silent);

		var count = 0;
		await foreach (var _ in orchestrator.BackfillRolledOverIndicesAsync())
			count++;

		count.Should().Be(0, "no rollover → enumerable completes immediately with zero items");
	}

	[Test]
	public async Task BackfillIsSafeToCallUnconditionally()
	{
		using var orchestrator = CreateOrchestrator(Transport);
		await orchestrator.StartAsync(BootstrapMethod.Silent);

		// Multiple unconditional calls must not throw and must leave writes unblocked
		await foreach (var _ in orchestrator.BackfillRolledOverIndicesAsync()) { }
		await foreach (var _ in orchestrator.BackfillRolledOverIndicesAsync()) { }

		orchestrator.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow })
			.Should().BeTrue("writes should proceed after noop backfill");
	}

	// ── CompleteAsync ────────────────────────────────────────────────────

	[Test]
	public async Task CompleteAsyncWithNoWritesReturnsTrueQuickly()
	{
		using var orchestrator = CreateOrchestrator(Transport);
		await orchestrator.StartAsync(BootstrapMethod.Silent);

		var sw = Stopwatch.StartNew();
		var result = await orchestrator.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(10));
		sw.Stop();

		result.Should().BeTrue();
		sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8));
	}

	[Test]
	public async Task OnPostCompleteIsInvokedDuringComplete()
	{
		DeltaOrchestratorContext<TestDocument>? captured = null;
		using var orchestrator = new DeltaSyncOrchestrator<TestDocument>(
			Transport,
			CreateBatchContext("delta-primary"),
			CreateBatchContext("delta-secondary"))
		{
			OnPostComplete = (ctx, _, _) =>
			{
				captured = ctx;
				return Task.CompletedTask;
			}
		};

		await orchestrator.StartAsync(BootstrapMethod.Silent);
		await orchestrator.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(5));

		captured.Should().NotBeNull();
		captured!.Strategy.Should().Be(orchestrator.Strategy);
		captured.BatchTimestamp.Should().Be(orchestrator.BatchTimestamp);
	}

	// ── Lifecycle ────────────────────────────────────────────────────────

	[Test]
	public void DisposeBeforeStartDoesNotThrow()
	{
		var orchestrator = CreateOrchestrator(Transport);
		orchestrator.Dispose();
	}

	[Test]
	public async Task DisposeAfterStartDoesNotThrow()
	{
		var orchestrator = CreateOrchestrator(Transport);
		await orchestrator.StartAsync(BootstrapMethod.Silent);
		orchestrator.Dispose();
		orchestrator.Dispose();
	}

	[Test]
	public void DiagnosticsListenerIsNull()
	{
		using var orchestrator = CreateOrchestrator(Transport);
		orchestrator.DiagnosticsListener.Should().BeNull();
	}

	[Test]
	public async Task ConfigurePrimaryAndConfigureSecondaryAreInvoked()
	{
		var primaryInvoked = false;
		var secondaryInvoked = false;
		using var orchestrator = new DeltaSyncOrchestrator<TestDocument>(
			Transport,
			CreateBatchContext("delta-primary"),
			CreateBatchContext("delta-secondary"))
		{
			ConfigurePrimary = _ => primaryInvoked = true,
			ConfigureSecondary = _ => secondaryInvoked = true,
		};

		await orchestrator.StartAsync(BootstrapMethod.Silent);
		primaryInvoked.Should().BeTrue();
		secondaryInvoked.Should().BeTrue();
	}

	[Test]
	public async Task AddPreBootstrapTaskReturnsSelfForChaining()
	{
		using var orchestrator = CreateOrchestrator(Transport);
		var result = orchestrator
			.AddPreBootstrapTask((_, _) => Task.CompletedTask)
			.AddPreBootstrapTask((_, _) => Task.CompletedTask);

		result.Should().BeSameAs(orchestrator);
		await orchestrator.StartAsync(BootstrapMethod.Silent);
	}
}
