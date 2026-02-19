// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Mapping;
using Elastic.Transport;
using Elastic.Transport.VirtualizedCluster;
using Elastic.Transport.VirtualizedCluster.Components;
using Elastic.Transport.VirtualizedCluster.Rules;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class IncrementalSyncOrchestratorTests
{
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

	private static DistributedTransport<ITransportConfiguration> CreateTransport(Func<VirtualCluster, VirtualCluster> setup)
	{
		var cluster = Virtual.Elasticsearch
			.Bootstrap(numberOfNodes: 1)
			.Ping(c => c.SucceedAlways());

		var virtualSettings = setup(cluster)
			.StaticNodePool()
			.Settings(s => s.DisablePing());

		var settings = new TransportConfigurationDescriptor(virtualSettings.ConnectionPool, virtualSettings.Connection)
			.DisablePing()
			.EnableDebugMode();

		return new DistributedTransport(settings);
	}

	/// <summary>
	/// Creates a transport that returns task-compatible JSON for all responses.
	/// The response body contains both "task" (for starting async ops) and "completed" (for polling).
	/// </summary>
	private static DistributedTransport<ITransportConfiguration> CreateTransportWithTaskResponse()
	{
		var responseBytes = Encoding.UTF8.GetBytes("""{"task":"n:1","completed":true}""");
		var connection = VirtualClusterRequestInvoker.Success(responseBytes);
		var pool = new SingleNodePool(new Uri("http://localhost:9200"));
		var settings = new TransportConfigurationDescriptor(pool, connection)
			.DisablePing()
			.EnableDebugMode();
		return new DistributedTransport(settings);
	}

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
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		using var orchestrator = CreateOrchestrator(transport);

		orchestrator.Strategy.Should().Be(IngestSyncStrategy.Reindex);
		orchestrator.LastUpdatedField.Should().Be("last_updated");
		orchestrator.BatchIndexDateField.Should().Be("batch_index_date");
		orchestrator.BatchTimestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
	}

	[Test]
	public void TryWriteBeforeStartThrows()
	{
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		using var orchestrator = CreateOrchestrator(transport);

		var act = () => orchestrator.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*StartAsync*");
	}

	[Test]
	public async Task StartSelectsMultiplexWhenHashesDiffer()
	{
		// Server returns empty hash (no existing template) -> hash mismatch -> Multiplex
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		using var orchestrator = CreateOrchestrator(transport);

		var strategy = await orchestrator.StartAsync(BootstrapMethod.Silent);
		strategy.Should().Be(IngestSyncStrategy.Multiplex);
		orchestrator.Strategy.Should().Be(IngestSyncStrategy.Multiplex);
	}

	[Test]
	public async Task StartExecutesPreBootstrapTasks()
	{
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		using var orchestrator = CreateOrchestrator(transport);

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
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		using var orchestrator = CreateOrchestrator(transport);

		var result = orchestrator
			.AddPreBootstrapTask((_, _) => Task.CompletedTask)
			.AddPreBootstrapTask((_, _) => Task.CompletedTask);

		result.Should().BeSameAs(orchestrator);
		await orchestrator.StartAsync(BootstrapMethod.Silent);
	}

	[Test]
	public async Task MultiplexWritesToBothChannels()
	{
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		using var orchestrator = CreateOrchestrator(transport);

		await orchestrator.StartAsync(BootstrapMethod.Silent);
		orchestrator.Strategy.Should().Be(IngestSyncStrategy.Multiplex);

		var result = orchestrator.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		result.Should().BeTrue();
	}

	[Test]
	public async Task TryWriteManyWritesMultipleDocuments()
	{
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		using var orchestrator = CreateOrchestrator(transport);

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
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		using var orchestrator = CreateOrchestrator(transport);

		await orchestrator.StartAsync(BootstrapMethod.Silent);

		var result = await orchestrator.WaitToWriteAsync(new TestDocument { Timestamp = DateTimeOffset.UtcNow });
		result.Should().BeTrue();
	}

	[Test]
	public async Task WaitToWriteManyAsyncWritesDocuments()
	{
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		using var orchestrator = CreateOrchestrator(transport);

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
		var transport = CreateTransportWithTaskResponse();
		using var orchestrator = CreateSimpleOrchestrator(transport);

		OrchestratorContext<TestDocument>? capturedContext = null;
		orchestrator.OnPostComplete = (ctx, ct) =>
		{
			capturedContext = ctx;
			return Task.CompletedTask;
		};

		await orchestrator.StartAsync(BootstrapMethod.Silent);
		await orchestrator.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(5));

		capturedContext.Should().NotBeNull();
		capturedContext!.Transport.Should().BeSameAs(transport);
		capturedContext.Strategy.Should().Be(orchestrator.Strategy);
		capturedContext.BatchTimestamp.Should().Be(orchestrator.BatchTimestamp);
	}

	[Test]
	public async Task DisposeDisposesChannels()
	{
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		var orchestrator = CreateOrchestrator(transport);

		await orchestrator.StartAsync(BootstrapMethod.Silent);

		// Should not throw
		orchestrator.Dispose();

		// Calling Dispose again should be safe
		orchestrator.Dispose();
	}

	[Test]
	public void DisposeBeforeStartDoesNotThrow()
	{
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		var orchestrator = CreateOrchestrator(transport);
		orchestrator.Dispose();
	}

	[Test]
	public void DiagnosticsListenerIsNull()
	{
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		using var orchestrator = CreateOrchestrator(transport);

		orchestrator.DiagnosticsListener.Should().BeNull();
	}

	[Test]
	public async Task ConfigurePrimaryIsInvoked()
	{
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		using var orchestrator = CreateOrchestrator(transport);

		var invoked = false;
		orchestrator.ConfigurePrimary = opts =>
		{
			invoked = true;
		};

		await orchestrator.StartAsync(BootstrapMethod.Silent);
		invoked.Should().BeTrue();
	}

	[Test]
	public async Task ConfigureSecondaryIsInvoked()
	{
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		using var orchestrator = CreateOrchestrator(transport);

		var invoked = false;
		orchestrator.ConfigureSecondary = opts =>
		{
			invoked = true;
		};

		await orchestrator.StartAsync(BootstrapMethod.Silent);
		invoked.Should().BeTrue();
	}

	[Test]
	public async Task CompleteAsyncMultiplexDrainsBothChannels()
	{
		var transport = CreateTransportWithTaskResponse();
		using var orchestrator = CreateSimpleOrchestrator(transport);

		await orchestrator.StartAsync(BootstrapMethod.Silent);
		orchestrator.Strategy.Should().Be(IngestSyncStrategy.Multiplex);

		orchestrator.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UtcNow });

		var result = await orchestrator.CompleteAsync(drainMaxWait: TimeSpan.FromSeconds(10));
		result.Should().BeTrue();
	}

	[Test]
	public async Task DrainAllAsyncDoesNotThrow()
	{
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		using var orchestrator = CreateOrchestrator(transport);

		await orchestrator.StartAsync(BootstrapMethod.Silent);

		await orchestrator.DrainAllAsync(TimeSpan.FromSeconds(5));
	}

	[Test]
	public async Task RefreshAllAsyncReturnsResult()
	{
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		using var orchestrator = CreateOrchestrator(transport);

		await orchestrator.StartAsync(BootstrapMethod.Silent);

		var result = await orchestrator.RefreshAllAsync();
		result.Should().BeTrue();
	}

	[Test]
	public async Task ApplyAllAliasesAsyncReturnsResult()
	{
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		using var orchestrator = CreateSimpleOrchestrator(transport);

		await orchestrator.StartAsync(BootstrapMethod.Silent);

		var result = await orchestrator.ApplyAllAliasesAsync();
		result.Should().BeTrue();
	}

	[Test]
	public async Task MultiplePreBootstrapTasksAllExecuteInOrder()
	{
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		using var orchestrator = CreateOrchestrator(transport);

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
		var transport = CreateTransport(v => v.ClientCalls(c => c.SucceedAlways()));
		var primary = new ElasticsearchTypeContext(
			() => "{}", () => "{}", () => "{}",
			"hash", "sh", "mh",
			new IndexStrategy(), // no WriteTarget set
			new SearchStrategy(),
			EntityTarget.Index
		);
		var secondary = CreateTypeContext("secondary", "sr", "s-*");
		using var orchestrator = new IncrementalSyncOrchestrator<TestDocument>(transport, primary, secondary);

		var act = async () => await orchestrator.StartAsync(BootstrapMethod.Silent);
		act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("*WriteTarget*");
	}
}
