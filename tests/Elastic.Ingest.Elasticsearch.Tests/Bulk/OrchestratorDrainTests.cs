// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using Elastic.Mapping;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests.Bulk;

/// <summary>
/// Finishing a sync deletes old documents or points aliases at the new index. Both are only right when every document of the
/// run reached Elasticsearch, so a drain that did not finish must stop the completion before it changes anything.
/// </summary>
public class OrchestratorDrainTests
{
	private static ElasticsearchTypeContext Context(string writeTarget, string readAlias, string pattern) =>
		new(
			() => "{}", () => "{}", () => "{}", "test-hash", "settings-hash", "mappings-hash",
			new IndexStrategy { WriteTarget = writeTarget, DatePattern = "yyyy.MM.dd.HHmmss" },
			new SearchStrategy { ReadAlias = readAlias, Pattern = pattern },
			EntityTarget.Index,
			GetId: static obj => ((TestDocument)obj).Timestamp.ToString("o"),
			GetTimestamp: static obj => ((TestDocument)obj).Timestamp,
			MappedType: typeof(TestDocument),
			SetBatchIndexDate: static (_, _) => { },
			SetLastUpdated: static (_, _) => { },
			BatchIndexDateFieldName: "batch_index_date",
			LastUpdatedFieldName: "last_updated");

	// every bulk request takes a while to answer, everything else is answered at once
	private static ScriptedTransport SlowBulk() => new((_, r) => r.Path.EndsWith("_bulk", StringComparison.Ordinal)
		? ScriptedResponse.Items(Enumerable.Repeat(201, ScriptedTransport.CountOperations(r)).ToArray()).After(TimeSpan.FromSeconds(3))
		: r.Path.Contains("_tasks", StringComparison.Ordinal) ? ScriptedResponse.Raw("{\"completed\":true}")
		: r.Path.Contains("_delete_by_query", StringComparison.Ordinal) || r.Path.Contains("_reindex", StringComparison.Ordinal) ? ScriptedResponse.Raw("{\"task\":\"n:1\"}")
		: ScriptedResponse.Raw("{}"));

	private static bool ChangesTheCluster(CapturedRequest r) =>
		r.Path.Contains("_delete_by_query", StringComparison.Ordinal)
		|| r.Path.Contains("_reindex", StringComparison.Ordinal)
		|| r.Path.Contains("_aliases", StringComparison.Ordinal);

	[Test]
	public async Task IncrementalCompleteChangesNothingWhenTheDrainDidNotFinish()
	{
		var t = SlowBulk();
		using var orchestrator = new IncrementalSyncOrchestrator<TestDocument>(t.Transport,
			Context("primary-docs", "primary-search", "primary-docs-*"), Context("secondary-docs", "secondary-search", "secondary-docs-*"));
		_ = await orchestrator.StartAsync(BootstrapMethod.Silent);
		for (var i = 0; i < 3; i++) orchestrator.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(i) }).Should().BeTrue();
		var requestsBefore = t.Requests.Count;

		var completed = await orchestrator.CompleteAsync(drainMaxWait: TimeSpan.FromMilliseconds(300));

		completed.Should().BeFalse("the documents were still being written");
		t.Requests.Skip(requestsBefore).Where(ChangesTheCluster).Should().BeEmpty("nothing may be deleted or switched over while documents are in flight");
	}

	[Test]
	public async Task DeltaCompleteChangesNothingWhenTheDrainDidNotFinish()
	{
		var t = SlowBulk();
		using var orchestrator = new DeltaSyncOrchestrator<TestDocument>(t.Transport,
			Context("primary-docs", "primary-search", "primary-docs-*"), Context("secondary-docs", "secondary-search", "secondary-docs-*"));
		_ = await orchestrator.StartAsync(BootstrapMethod.Silent);
		for (var i = 0; i < 3; i++) orchestrator.TryWrite(new TestDocument { Timestamp = DateTimeOffset.UnixEpoch.AddSeconds(i) }).Should().BeTrue();
		var requestsBefore = t.Requests.Count;

		var completed = await orchestrator.CompleteAsync(drainMaxWait: TimeSpan.FromMilliseconds(300));

		completed.Should().BeFalse("the documents were still being written");
		t.Requests.Skip(requestsBefore).Where(ChangesTheCluster).Should().BeEmpty("no alias may point at an index that is still being filled");
	}
}
