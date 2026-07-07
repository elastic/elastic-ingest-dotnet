// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Nodes;
using Elastic.Ingest.Elasticsearch.Helpers;
using Elastic.Transport;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class ServerReindexProgressTests
{
	private static JsonResponse MakeResponse(string json) => new(JsonNode.Parse(json)!);

	[Test]
	public void ReindexApiParsesDocumentFailures()
	{
		var response = MakeResponse("""
		{
			"completed": true,
			"id": "reindex-abc",
			"status": {
				"total": 100,
				"created": 97,
				"updated": 0,
				"deleted": 0,
				"noops": 0,
				"version_conflicts": 0
			},
			"running_time_in_nanos": 5000000000,
			"response": {
				"failures": [
					{
						"index": "my-index",
						"id": "doc1",
						"cause": { "type": "mapper_parsing_exception", "reason": "failed to parse field [age]" },
						"status": 400
					},
					{
						"index": "my-index",
						"id": "doc2",
						"cause": { "type": "mapper_parsing_exception", "reason": "failed to parse field [name]" },
						"status": 400
					},
					{
						"index": "other-index",
						"id": "doc3",
						"cause": { "type": "version_conflict_engine_exception", "reason": "version conflict" },
						"status": 409
					}
				]
			}
		}
		""");

		var progress = ServerReindex.ParseProgress("task-1", response, isReindexApi: true);

		progress.Failures.Should().HaveCount(3);
		progress.Failures[0].Index.Should().Be("my-index");
		progress.Failures[0].Id.Should().Be("doc1");
		progress.Failures[0].Status.Should().Be(400);
		progress.Failures[0].CauseType.Should().Be("mapper_parsing_exception");
		progress.Failures[0].CauseReason.Should().Be("failed to parse field [age]");

		progress.Failures[2].Index.Should().Be("other-index");
		progress.Failures[2].Status.Should().Be(409);

		progress.Error.Should().Contain("3 document(s) failed");
		progress.Error.Should().Contain("failed to parse field [age]");
	}

	[Test]
	public void TaskApiParsesDocumentFailures()
	{
		var response = MakeResponse("""
		{
			"completed": true,
			"task": {
				"status": {
					"total": 50,
					"created": 48,
					"updated": 0,
					"deleted": 0,
					"noops": 0,
					"version_conflicts": 0
				},
				"running_time_in_nanos": 2000000000
			},
			"response": {
				"failures": [
					{
						"index": "legacy-index",
						"id": "docA",
						"cause": { "type": "strict_dynamic_mapping_exception", "reason": "mapping set to strict" },
						"status": 400
					}
				]
			}
		}
		""");

		var progress = ServerReindex.ParseProgress("n:42", response, isReindexApi: false);

		progress.Failures.Should().HaveCount(1);
		progress.Failures[0].Index.Should().Be("legacy-index");
		progress.Failures[0].Id.Should().Be("docA");
		progress.Failures[0].CauseType.Should().Be("strict_dynamic_mapping_exception");
		progress.Failures[0].CauseReason.Should().Be("mapping set to strict");

		progress.Error.Should().Contain("1 document(s) failed");
	}

	[Test]
	public void TaskLevelErrorTakesPrecedenceOverFailures()
	{
		var response = MakeResponse("""
		{
			"completed": true,
			"error": { "reason": "task-level failure" },
			"status": {
				"total": 10,
				"created": 0,
				"updated": 0,
				"deleted": 0,
				"noops": 0,
				"version_conflicts": 0
			},
			"running_time_in_nanos": 1000000000,
			"response": {
				"failures": [
					{
						"index": "idx",
						"id": "d1",
						"cause": { "type": "some_error", "reason": "per-doc error" },
						"status": 400
					}
				]
			}
		}
		""");

		var progress = ServerReindex.ParseProgress("t:1", response, isReindexApi: true);

		progress.Error.Should().Be("task-level failure");
		progress.Failures.Should().HaveCount(1);
	}

	[Test]
	public void NoFailuresProducesEmptyList()
	{
		var response = MakeResponse("""
		{
			"completed": true,
			"status": {
				"total": 10,
				"created": 10,
				"updated": 0,
				"deleted": 0,
				"noops": 0,
				"version_conflicts": 0
			},
			"running_time_in_nanos": 500000000,
			"response": {
				"failures": []
			}
		}
		""");

		var progress = ServerReindex.ParseProgress("t:1", response, isReindexApi: true);

		progress.Failures.Should().BeEmpty();
		progress.Error.Should().BeNull();
	}

	[Test]
	public void MissingFailuresFieldProducesEmptyList()
	{
		var response = MakeResponse("""
		{
			"completed": true,
			"status": {
				"total": 10,
				"created": 10,
				"updated": 0,
				"deleted": 0,
				"noops": 0,
				"version_conflicts": 0
			},
			"running_time_in_nanos": 500000000,
			"response": {}
		}
		""");

		var progress = ServerReindex.ParseProgress("t:1", response, isReindexApi: true);

		progress.Failures.Should().BeEmpty();
		progress.Error.Should().BeNull();
	}

	[Test]
	public void InProgressTaskDoesNotParseFailures()
	{
		var response = MakeResponse("""
		{
			"completed": false,
			"status": {
				"total": 100,
				"created": 50,
				"updated": 0,
				"deleted": 0,
				"noops": 0,
				"version_conflicts": 0
			},
			"running_time_in_nanos": 3000000000
		}
		""");

		var progress = ServerReindex.ParseProgress("t:1", response, isReindexApi: true);

		progress.Failures.Should().BeEmpty();
		progress.IsCompleted.Should().BeFalse();
	}

	[Test]
	public void FailureWithMissingFieldsHandledGracefully()
	{
		var response = MakeResponse("""
		{
			"completed": true,
			"status": {
				"total": 5,
				"created": 4,
				"updated": 0,
				"deleted": 0,
				"noops": 0,
				"version_conflicts": 0
			},
			"running_time_in_nanos": 100000000,
			"response": {
				"failures": [
					{ "index": "idx" }
				]
			}
		}
		""");

		var progress = ServerReindex.ParseProgress("t:1", response, isReindexApi: true);

		progress.Failures.Should().HaveCount(1);
		progress.Failures[0].Index.Should().Be("idx");
		progress.Failures[0].Id.Should().BeNull();
		progress.Failures[0].Status.Should().BeNull();
		progress.Failures[0].CauseType.Should().BeNull();
		progress.Failures[0].CauseReason.Should().BeNull();

		progress.Error.Should().Contain("1 document(s) failed");
		progress.Error.Should().Contain("unknown");
	}
}
