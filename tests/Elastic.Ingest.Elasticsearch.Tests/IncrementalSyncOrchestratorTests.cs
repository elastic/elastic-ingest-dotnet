// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Mapping;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class IncrementalSyncOrchestratorTests
{
	private static ITransport Transport => TestSetup.SharedTransport;

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
			new IndexStrategy
			{
				WriteTarget = writeTarget,
				DatePattern = datePattern
			},
			new SearchStrategy
			{
				ReadAlias = readAlias,
				Pattern = searchPattern
			},
			EntityTarget.Index,
			GetId: static obj => ((TestDocument)obj).Timestamp.ToString("o"),
			GetTimestamp: static obj => ((TestDocument)obj).Timestamp,
			MappedType: typeof(TestDocument)
		);

	private static ElasticsearchTypeContext CreateSimpleTypeContext(string writeTarget) =>
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
			MappedType: typeof(TestDocument)
		);

	private static IncrementalSyncOrchestrator<TestDocument> CreateOrchestrator(ITransport transport)
	{
		var primary = CreateTypeContext("primary-docs", "primary-search", "primary-docs-*");
		var secondary = CreateTypeContext("secondary-docs", "secondary-search", "secondary-docs-*");
		return new IncrementalSyncOrchestrator<TestDocument>(transport, primary, secondary);
	}

	private static IncrementalSyncOrchestrator<TestDocument> CreateSimpleOrchestrator(ITransport transport)
	{
		var primary = CreateSimpleTypeContext("primary-docs");
		var secondary = CreateSimpleTypeContext("secondary-docs");
		return new IncrementalSyncOrchestrator<TestDocument>(transport, primary, secondary);
	}

	[Test]
	public void ConstructorSetsDefaults()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		orchestrator.Strategy.Should().Be(IngestSyncStrategy.Multiplex);
		orchestrator.LastUpdatedField.Should().Be("last_updated");
		orchestrator.BatchIndexDateField.Should().Be("batch_index_date");
		orchestrator.BatchTimestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
	}

	[Test]
	public void TryWriteBeforeStartThrows()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		var act = () => orchestrator.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*StartAsync*");
	}

	[Test]
	public async Task StartSelectsMultiplexWhenSecondaryRolledOver()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		var context = await orchestrator.StartAsync(BootstrapMethod.Silent);
		context.Strategy.Should().Be(IngestSyncStrategy.Multiplex);
		orchestrator.Strategy.Should().Be(IngestSyncStrategy.Multiplex);
	}

	[Test]
	public async Task RolloverInfoIsPopulatedOnContext()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		var context = await orchestrator.StartAsync(BootstrapMethod.Silent);

		context.PrimaryRollover.Should().NotBeNull();
		context.PrimaryRollover!.Label.Should().Be("primary");
		context.PrimaryRollover.LocalHash.Should().NotBeNullOrEmpty();
		context.PrimaryRollover.RolledOver.Should().BeTrue(
			"mock transport returns empty hash → local != remote → rolled over");

		context.SecondaryRollover.Should().NotBeNull();
		context.SecondaryRollover!.Label.Should().Be("secondary");
		context.SecondaryRollover.LocalHash.Should().NotBeNullOrEmpty();
		context.SecondaryRollover.RolledOver.Should().BeTrue();
	}

	[Test]
	public async Task OnRolloverDecisionIsInvokedForBothIndices()
	{
		var primary = CreateTypeContext("primary-docs", "primary-search", "primary-docs-*");
		var secondary = CreateTypeContext("secondary-docs", "secondary-search", "secondary-docs-*");

		var decisions = new List<IndexRolloverInfo>();
		using var orchestrator = new IncrementalSyncOrchestrator<TestDocument>(Transport, primary, secondary)
		{
			OnRolloverDecision = info => decisions.Add(info)
		};

		await orchestrator.StartAsync(BootstrapMethod.Silent);

		decisions.Should().HaveCount(2);
		decisions[0].Label.Should().Be("primary");
		decisions[1].Label.Should().Be("secondary");
		decisions.Should().AllSatisfy(d =>
		{
			d.LocalHash.Should().NotBeNullOrEmpty();
			d.RolledOver.Should().BeTrue("mock returns empty hash → always rolled over");
		});
	}

	[Test]
	public async Task StartExecutesPreBootstrapTasks()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		var taskExecuted = false;
		orchestrator.AddPreBootstrapTask(async (t, ct) =>
		{
			taskExecuted = true;
			await Task.CompletedTask;
		});

		await orchestrator.StartAsync(BootstrapMethod.Silent);
		taskExecuted.Should().BeTrue();
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

	[Test]
	public async Task MultiplexWritesToBothChannels()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		await orchestrator.StartAsync(BootstrapMethod.Silent);
		orchestrator.Strategy.Should().Be(IngestSyncStrategy.Multiplex);

		var result = orchestrator.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		result.Should().BeTrue();
	}

	[Test]
	public async Task TryWriteManyWritesMultipleDocuments()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		await orchestrator.StartAsync(BootstrapMethod.Silent);

		var docs = new[]
		{
			new TestDocument { Timestamp = DateTimeOffset.UtcNow },
			new TestDocument { Timestamp = DateTimeOffset.UtcNow }
		};
		var result = orchestrator.TryWriteMany(docs);
		result.Should().BeTrue();
	}

	[Test]
	public async Task WaitToWriteAsyncWritesDocument()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		await orchestrator.StartAsync(BootstrapMethod.Silent);

		var result = await orchestrator.WaitToWriteAsync(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		result.Should().BeTrue();
	}

	[Test]
	public async Task WaitToWriteManyAsyncWritesDocuments()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		await orchestrator.StartAsync(BootstrapMethod.Silent);

		var docs = new[]
		{
			new TestDocument { Timestamp = DateTimeOffset.UtcNow },
			new TestDocument { Timestamp = DateTimeOffset.UtcNow }
		};
		var result = await orchestrator.WaitToWriteManyAsync(docs);
		result.Should().BeTrue();
	}

	[Test]
	public async Task OnPostCompleteIsCalledDuringComplete()
	{
		var primary = CreateSimpleTypeContext("primary-docs");
		var secondary = CreateSimpleTypeContext("secondary-docs");

		OrchestratorContext<TestDocument>? capturedContext = null;
		ITransport? capturedTransport = null;
		using var orchestrator = new IncrementalSyncOrchestrator<TestDocument>(Transport, primary, secondary)
		{
			OnPostComplete = (ctx, t, ct) =>
			{
				capturedContext = ctx;
				capturedTransport = t;
				return Task.CompletedTask;
			}
		};

		await orchestrator.StartAsync(BootstrapMethod.Silent);
		await orchestrator.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(5));

		capturedContext.Should().NotBeNull();
		capturedTransport.Should().BeSameAs(Transport);
		capturedContext!.Strategy.Should().Be(orchestrator.Strategy);
		capturedContext.BatchTimestamp.Should().Be(orchestrator.BatchTimestamp);
	}

	[Test]
	public async Task DisposeDisposesChannels()
	{
		var orchestrator = CreateOrchestrator(Transport);

		await orchestrator.StartAsync(BootstrapMethod.Silent);

		orchestrator.Dispose();
		orchestrator.Dispose();
	}

	[Test]
	public void DisposeBeforeStartDoesNotThrow()
	{
		var orchestrator = CreateOrchestrator(Transport);
		orchestrator.Dispose();
	}

	[Test]
	public void DiagnosticsListenerIsNull()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		orchestrator.DiagnosticsListener.Should().BeNull();
	}

	[Test]
	public async Task ConfigurePrimaryIsInvoked()
	{
		var primary = CreateTypeContext("primary-docs", "primary-search", "primary-docs-*");
		var secondary = CreateTypeContext("secondary-docs", "secondary-search", "secondary-docs-*");

		var invoked = false;
		using var orchestrator = new IncrementalSyncOrchestrator<TestDocument>(Transport, primary, secondary)
		{
			ConfigurePrimary = _ => invoked = true
		};

		await orchestrator.StartAsync(BootstrapMethod.Silent);
		invoked.Should().BeTrue();
	}

	[Test]
	public async Task ConfigureSecondaryIsInvoked()
	{
		var primary = CreateTypeContext("primary-docs", "primary-search", "primary-docs-*");
		var secondary = CreateTypeContext("secondary-docs", "secondary-search", "secondary-docs-*");

		var invoked = false;
		using var orchestrator = new IncrementalSyncOrchestrator<TestDocument>(Transport, primary, secondary)
		{
			ConfigureSecondary = _ => invoked = true
		};

		await orchestrator.StartAsync(BootstrapMethod.Silent);
		invoked.Should().BeTrue();
	}

	[Test]
	public async Task CompleteAsyncMultiplexDrainsBothChannels()
	{
		using var orchestrator = CreateSimpleOrchestrator(Transport);

		await orchestrator.StartAsync(BootstrapMethod.Silent);
		orchestrator.Strategy.Should().Be(IngestSyncStrategy.Multiplex);

		orchestrator.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });

		var result = await orchestrator.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(10));
		result.Should().BeTrue();
	}

	[Test]
	public async Task CompleteAsyncWithNoWritesReturnsTrueQuickly()
	{
		using var orchestrator = CreateSimpleOrchestrator(Transport);
		await orchestrator.StartAsync(BootstrapMethod.Silent);
		orchestrator.Strategy.Should().Be(IngestSyncStrategy.Multiplex);
		var sw = Stopwatch.StartNew();
		var result = await orchestrator.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(10)).ConfigureAwait(false);
		sw.Stop();
		result.Should().BeTrue();
		sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8));
	}

	[Test]
	public async Task DrainAllAsyncDoesNotThrow()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		await orchestrator.StartAsync(BootstrapMethod.Silent);

		await orchestrator.DrainAllAsync(TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task RefreshAllAsyncReturnsResult()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		await orchestrator.StartAsync(BootstrapMethod.Silent);

		var result = await orchestrator.RefreshAllAsync();
		result.Should().BeTrue();
	}

	[Test]
	public async Task ApplyAllAliasesAsyncReturnsResult()
	{
		using var orchestrator = CreateSimpleOrchestrator(Transport);

		await orchestrator.StartAsync(BootstrapMethod.Silent);

		var result = await orchestrator.ApplyAllAliasesAsync();
		result.Should().BeTrue();
	}

	[Test]
	public async Task MultiplePreBootstrapTasksAllExecuteInOrder()
	{
		using var orchestrator = CreateOrchestrator(Transport);

		var executionOrder = new List<int>();
		orchestrator.AddPreBootstrapTask(async (t, ct) =>
		{
			executionOrder.Add(1);
			await Task.CompletedTask;
		});
		orchestrator.AddPreBootstrapTask(async (t, ct) =>
		{
			executionOrder.Add(2);
			await Task.CompletedTask;
		});
		orchestrator.AddPreBootstrapTask(async (t, ct) =>
		{
			executionOrder.Add(3);
			await Task.CompletedTask;
		});

		await orchestrator.StartAsync(BootstrapMethod.Silent);

		executionOrder.Should().Equal(1, 2, 3);
	}

	[Test]
	public void MissingWriteTargetThrows()
	{
		var primary = new ElasticsearchTypeContext(
			() => "{}", () => "{}", () => "{}",
			"hash", "sh", "mh",
			new IndexStrategy(),
			new SearchStrategy(),
			EntityTarget.Index
		);
		var secondary = CreateTypeContext("secondary", "sr", "s-*");
		using var orchestrator = new IncrementalSyncOrchestrator<TestDocument>(Transport, primary, secondary);

		var act = async () => await orchestrator.StartAsync(BootstrapMethod.Silent);
		act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("*WriteTarget*");
	}

	[Test]
	public async Task RawContextConstructorUsesBatchTimestampForIndexName()
	{
		var primary = CreateTypeContext("primary-docs", "primary-search", "primary-docs-*");
		var secondary = CreateTypeContext("secondary-docs", "secondary-search", "secondary-docs-*");

		using var orchestrator = new IncrementalSyncOrchestrator<TestDocument>(
			Transport, primary, secondary);

		var context = await orchestrator.StartAsync(BootstrapMethod.Silent);

		context.BatchTimestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5),
			"batch timestamp should be current time, not DateTimeOffset.MinValue");
		context.BatchTimestamp.Year.Should().BeGreaterThan(2000,
			"index names must use the batch timestamp, not default(DateTimeOffset) which produces 0001.01.01");
	}
}
