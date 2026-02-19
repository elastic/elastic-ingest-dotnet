// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Linq;
using Elastic.Ingest.Elasticsearch.Strategies;
using Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;
using Elastic.Mapping;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class StrategyFactoryTests
{
	private static ElasticsearchTypeContext CreateIndexContext(string writeTarget = "my-docs") =>
		new(
			() => "{}", () => "{}", () => "{}",
			"hash", "sh", "mh",
			new IndexStrategy { WriteTarget = writeTarget },
			new SearchStrategy(),
			EntityTarget.Index,
			MappedType: typeof(TestDocument)
		);

	private static ElasticsearchTypeContext CreateDataStreamContext() =>
		new(
			() => "{}", () => "{}", () => "{}",
			"hash", "sh", "mh",
			new IndexStrategy { Type = "logs", Dataset = "myapp", Namespace = "default", DataStreamName = "logs-myapp-default" },
			new SearchStrategy(),
			EntityTarget.DataStream,
			MappedType: typeof(TestDocument)
		);

	private static ElasticsearchTypeContext CreateWiredStreamContext() =>
		new(
			() => "{}", () => "{}", () => "{}",
			"hash", "sh", "mh",
			new IndexStrategy(),
			new SearchStrategy(),
			EntityTarget.WiredStream,
			MappedType: typeof(TestDocument)
		);

	private static ElasticsearchTypeContext CreateIndexContextWithAlias(
		string writeTarget, string readAlias, string datePattern = "yyyy.MM.dd") =>
		new(
			() => "{}", () => "{}", () => "{}",
			"hash", "sh", "mh",
			new IndexStrategy { WriteTarget = writeTarget, DatePattern = datePattern },
			new SearchStrategy { ReadAlias = readAlias },
			EntityTarget.Index,
			MappedType: typeof(TestDocument)
		);

	private static ElasticsearchTypeContext CreateIndexContextWithContentHash(string writeTarget) =>
		new(
			() => "{}", () => "{}", () => "{}",
			"hash", "sh", "mh",
			new IndexStrategy { WriteTarget = writeTarget },
			new SearchStrategy(),
			EntityTarget.Index,
			GetContentHash: static _ => "some-hash",
			MappedType: typeof(TestDocument)
		);

	// --- IngestStrategies.ForContext ---

	[Test]
	public void ForContextSelectsIndexForIndexTarget()
	{
		var tc = CreateIndexContext();
		var strategy = IngestStrategies.ForContext<TestDocument>(tc);

		strategy.DocumentIngest.Should().BeOfType<TypeContextIndexIngestStrategy<TestDocument>>();
		strategy.Bootstrap.Should().BeOfType<DefaultBootstrapStrategy>();
		strategy.Provisioning.Should().BeOfType<AlwaysCreateProvisioning>();
		strategy.AliasStrategy.Should().BeOfType<NoAliasStrategy>();
	}

	[Test]
	public void ForContextSelectsDataStreamForDataStreamTarget()
	{
		var tc = CreateDataStreamContext();
		var strategy = IngestStrategies.ForContext<TestDocument>(tc);

		strategy.DocumentIngest.Should().BeOfType<DataStreamIngestStrategy<TestDocument>>();
		strategy.AliasStrategy.Should().BeOfType<NoAliasStrategy>();
	}

	[Test]
	public void ForContextSelectsWiredStreamForWiredStreamTarget()
	{
		var tc = CreateWiredStreamContext();
		var strategy = IngestStrategies.ForContext<TestDocument>(tc);

		strategy.DocumentIngest.Should().BeOfType<WiredStreamIngestStrategy<TestDocument>>();
		strategy.AliasStrategy.Should().BeOfType<NoAliasStrategy>();
		strategy.Provisioning.Should().BeOfType<AlwaysCreateProvisioning>();
	}

	// --- IngestStrategies.Index ---

	[Test]
	public void IndexStrategyResolvesTemplateNameFromWriteTarget()
	{
		var tc = CreateIndexContext("products");
		var strategy = IngestStrategies.Index<TestDocument>(tc);

		strategy.TemplateName.Should().Be("products-template");
		strategy.TemplateWildcard.Should().Be("products-*");
	}

	[Test]
	public void IndexStrategyResolvesAliasWhenReadAliasIsSet()
	{
		var tc = CreateIndexContextWithAlias("catalog", "catalog-search");
		var strategy = IngestStrategies.Index<TestDocument>(tc);

		strategy.AliasStrategy.Should().BeOfType<LatestAndSearchAliasStrategy>();
	}

	[Test]
	public void IndexStrategyUsesNoAliasWhenReadAliasIsNull()
	{
		var tc = CreateIndexContext();
		var strategy = IngestStrategies.Index<TestDocument>(tc);

		strategy.AliasStrategy.Should().BeOfType<NoAliasStrategy>();
	}

	[Test]
	public void IndexStrategyUsesHashBasedProvisioningWhenContentHashIsSet()
	{
		var tc = CreateIndexContextWithContentHash("catalog");
		var strategy = IngestStrategies.Index<TestDocument>(tc);

		strategy.Provisioning.Should().BeOfType<HashBasedReuseProvisioning>();
	}

	[Test]
	public void IndexStrategyAcceptsCustomBootstrap()
	{
		var tc = CreateIndexContext();
		var customBootstrap = BootstrapStrategies.IndexWithIlm("my-policy");
		var strategy = IngestStrategies.Index<TestDocument>(tc, customBootstrap);

		strategy.Bootstrap.Should().BeSameAs(customBootstrap);
	}

	// --- IngestStrategies.DataStream ---

	[Test]
	public void DataStreamStrategyResolvesTemplateName()
	{
		var tc = CreateDataStreamContext();
		var strategy = IngestStrategies.DataStream<TestDocument>(tc);

		strategy.TemplateName.Should().Be("logs-myapp");
		strategy.TemplateWildcard.Should().Be("logs-myapp-*");
	}

	[Test]
	public void DataStreamStrategyWithRetentionUsesLifecycleStep()
	{
		var tc = CreateDataStreamContext();
		var strategy = IngestStrategies.DataStream<TestDocument>(tc, "30d");

		var bootstrap = strategy.Bootstrap.Should().BeOfType<DefaultBootstrapStrategy>().Subject;
		bootstrap.Steps.Should().Contain(s => s is DataStreamLifecycleStep);
	}

	[Test]
	public void DataStreamStrategyThrowsWithoutDataStreamName()
	{
		var tc = CreateIndexContext(); // Index context, no DataStreamName
		var act = () => IngestStrategies.DataStream<TestDocument>(tc);

		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*DataStreamName*");
	}

	// --- IngestStrategies.WiredStream ---

	[Test]
	public void WiredStreamUsesNoopBootstrap()
	{
		var tc = CreateWiredStreamContext();
		var strategy = IngestStrategies.WiredStream<TestDocument>(tc);

		var bootstrap = strategy.Bootstrap.Should().BeOfType<DefaultBootstrapStrategy>().Subject;
		bootstrap.Steps.Should().ContainSingle().Which.Should().BeOfType<NoopBootstrapStep>();
	}

	// --- BootstrapStrategies ---

	[Test]
	public void DataStreamBootstrapHasCorrectSteps()
	{
		var bootstrap = BootstrapStrategies.DataStream();
		var strategy = bootstrap.Should().BeOfType<DefaultBootstrapStrategy>().Subject;

		strategy.Steps.Should().HaveCount(2);
		strategy.Steps[0].Should().BeOfType<ComponentTemplateStep>();
		strategy.Steps[1].Should().BeOfType<DataStreamTemplateStep>();
	}

	[Test]
	public void DataStreamBootstrapWithRetentionHasLifecycleStep()
	{
		var bootstrap = BootstrapStrategies.DataStream("90d");
		var strategy = bootstrap.Should().BeOfType<DefaultBootstrapStrategy>().Subject;

		strategy.Steps.Should().HaveCount(3);
		strategy.Steps[0].Should().BeOfType<ComponentTemplateStep>();
		strategy.Steps[1].Should().BeOfType<DataStreamLifecycleStep>();
		strategy.Steps[2].Should().BeOfType<DataStreamTemplateStep>();
	}

	[Test]
	public void DataStreamWithIlmHasIlmAndComponentAndTemplateSteps()
	{
		var bootstrap = BootstrapStrategies.DataStreamWithIlm("my-policy");
		var strategy = bootstrap.Should().BeOfType<DefaultBootstrapStrategy>().Subject;

		strategy.Steps.Should().HaveCount(3);
		strategy.Steps[0].Should().BeOfType<IlmPolicyStep>();
		strategy.Steps[1].Should().BeOfType<ComponentTemplateStep>();
		strategy.Steps[2].Should().BeOfType<DataStreamTemplateStep>();
	}

	[Test]
	public void IndexBootstrapHasCorrectSteps()
	{
		var bootstrap = BootstrapStrategies.Index();
		var strategy = bootstrap.Should().BeOfType<DefaultBootstrapStrategy>().Subject;

		strategy.Steps.Should().HaveCount(2);
		strategy.Steps[0].Should().BeOfType<ComponentTemplateStep>();
		strategy.Steps[1].Should().BeOfType<IndexTemplateStep>();
	}

	[Test]
	public void IndexWithIlmHasIlmAndComponentAndTemplateSteps()
	{
		var bootstrap = BootstrapStrategies.IndexWithIlm("7-days");
		var strategy = bootstrap.Should().BeOfType<DefaultBootstrapStrategy>().Subject;

		strategy.Steps.Should().HaveCount(3);
		strategy.Steps[0].Should().BeOfType<IlmPolicyStep>();
		strategy.Steps[1].Should().BeOfType<ComponentTemplateStep>();
		strategy.Steps[2].Should().BeOfType<IndexTemplateStep>();
	}

	[Test]
	public void NoneBootstrapHasSingleNoopStep()
	{
		var bootstrap = BootstrapStrategies.None();
		var strategy = bootstrap.Should().BeOfType<DefaultBootstrapStrategy>().Subject;

		strategy.Steps.Should().ContainSingle().Which.Should().BeOfType<NoopBootstrapStep>();
	}

	// --- ValidateStepOrdering ---

	[Test]
	public void ValidateStepOrderingThrowsWhenIlmAfterComponent()
	{
		var act = () => new DefaultBootstrapStrategy(
			new ComponentTemplateStep(),
			new IlmPolicyStep("policy", null, null),
			new IndexTemplateStep()
		);

		act.Should().Throw<ArgumentException>()
			.WithMessage("*IlmPolicyStep*precede*ComponentTemplateStep*");
	}

	[Test]
	public void ValidateStepOrderingThrowsWhenComponentAfterIndexTemplate()
	{
		var act = () => new DefaultBootstrapStrategy(
			new IndexTemplateStep(),
			new ComponentTemplateStep()
		);

		act.Should().Throw<ArgumentException>()
			.WithMessage("*ComponentTemplateStep*precede*IndexTemplateStep*");
	}

	[Test]
	public void ValidateStepOrderingThrowsWhenLifecycleAfterDataStreamTemplate()
	{
		var act = () => new DefaultBootstrapStrategy(
			new ComponentTemplateStep(),
			new DataStreamTemplateStep(),
			new DataStreamLifecycleStep("30d")
		);

		act.Should().Throw<ArgumentException>()
			.WithMessage("*DataStreamLifecycleStep*precede*DataStreamTemplateStep*");
	}

	[Test]
	public void ValidateStepOrderingAcceptsCorrectOrdering()
	{
		// Should not throw
		var strategy = new DefaultBootstrapStrategy(
			new IlmPolicyStep("policy", null, null),
			new ComponentTemplateStep("policy"),
			new DataStreamLifecycleStep("30d"),
			new DataStreamTemplateStep()
		);

		strategy.Steps.Should().HaveCount(4);
	}
}
