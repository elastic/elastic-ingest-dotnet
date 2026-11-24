// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.Strategies;
using Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;
using Elastic.Transport;
using FluentAssertions;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class IndexMappingPatchStepTests
{
	private const string Desired = """{"properties":{"reason":{"type":"keyword"},"reasons":{"type":"keyword"}}}""";
	private const string LiveOld = """{"idx":{"mappings":{"properties":{"reason":{"type":"keyword"}}}}}""";
	private const string LiveCurrent = """{"idx":{"mappings":{"properties":{"reason":{"type":"keyword"},"reasons":{"type":"keyword"}}}}}""";

	private sealed record Call(string Method, string Path, string Body);

	/// <summary> Answers every request with the same body and status, and records what was sent. </summary>
	private sealed class RecordingInvoker(string response, int statusCode) : IRequestInvoker
	{
		private readonly InMemoryRequestInvoker _inner = new(Encoding.UTF8.GetBytes(response), statusCode);
		public List<Call> Calls { get; } = [];

		public ResponseFactory ResponseFactory => _inner.ResponseFactory;

		public void Dispose() { }

		public TResponse Request<TResponse>(Endpoint endpoint, BoundConfiguration boundConfiguration, PostData postData)
			where TResponse : TransportResponse, new()
		{
			using var ms = new MemoryStream();
			postData?.Write(ms, boundConfiguration.ConnectionSettings, false);
			Calls.Add(new Call(endpoint.Method.ToString(), endpoint.PathAndQuery, Encoding.UTF8.GetString(ms.ToArray())));
			return _inner.Request<TResponse>(endpoint, boundConfiguration, postData);
		}

		public async Task<TResponse> RequestAsync<TResponse>(Endpoint endpoint, BoundConfiguration boundConfiguration, PostData postData, CancellationToken cancellationToken)
			where TResponse : TransportResponse, new()
		{
			using var ms = new MemoryStream();
			if (postData is not null)
				await postData.WriteAsync(ms, boundConfiguration.ConnectionSettings, false, cancellationToken).ConfigureAwait(false);
			Calls.Add(new Call(endpoint.Method.ToString(), endpoint.PathAndQuery, Encoding.UTF8.GetString(ms.ToArray())));
			return await _inner.RequestAsync<TResponse>(endpoint, boundConfiguration, postData, cancellationToken).ConfigureAwait(false);
		}
	}

	private static (BootstrapContext Context, RecordingInvoker Invoker) Create(
		string getResponse, int statusCode = 200, string indexName = "idx", BootstrapMethod method = BootstrapMethod.Failure)
	{
		var invoker = new RecordingInvoker(getResponse, statusCode);
		var transport = new DistributedTransport<TransportConfiguration>(
			new TransportConfiguration(new SingleNodePool(new Uri("http://localhost:9200")), invoker) { DisablePings = true });
		var context = new BootstrapContext
		{
			Transport = transport,
			BootstrapMethod = method,
			TemplateName = "idx",
			TemplateWildcard = "idx",
			IndexName = indexName,
			GetMappingsJson = () => Desired
		};
		return (context, invoker);
	}

	[Test]
	public async Task PutsOnlyTheMissingFields()
	{
		var (context, invoker) = Create(LiveOld);

		(await new IndexMappingPatchStep().ExecuteAsync(context)).Should().BeTrue();

		invoker.Calls.Select(c => c.Method).Should().Equal("GET", "PUT");
		invoker.Calls[1].Path.Should().Be("idx/_mapping");
		invoker.Calls[1].Body.Should().Be("""{"properties":{"reasons":{"type":"keyword"}}}""");
	}

	[Test]
	public void SyncPathPutsOnlyTheMissingFields()
	{
		var (context, invoker) = Create(LiveOld);

		new IndexMappingPatchStep().Execute(context).Should().BeTrue();

		invoker.Calls.Select(c => c.Method).Should().Equal("GET", "PUT");
		invoker.Calls[1].Body.Should().Be("""{"properties":{"reasons":{"type":"keyword"}}}""");
	}

	[Test]
	public async Task NoPutWhenTheIndexIsUpToDate()
	{
		var (context, invoker) = Create(LiveCurrent);

		(await new IndexMappingPatchStep().ExecuteAsync(context)).Should().BeTrue();

		invoker.Calls.Select(c => c.Method).Should().Equal("GET");
	}

	[Test]
	public async Task NothingHappensWhenTheIndexDoesNotExistYet()
	{
		var (context, invoker) = Create("""{"error":{"type":"index_not_found_exception"},"status":404}""", statusCode: 404);

		(await new IndexMappingPatchStep().ExecuteAsync(context)).Should().BeTrue();

		invoker.Calls.Select(c => c.Method).Should().Equal("GET");
	}

	[Test]
	public async Task NoRequestsWithoutAConcreteIndexName()
	{
		var (context, invoker) = Create(LiveOld, indexName: null);

		(await new IndexMappingPatchStep().ExecuteAsync(context)).Should().BeTrue();

		invoker.Calls.Should().BeEmpty();
	}

	[Test]
	public async Task ReadFailureThrowsOnFailureAndReturnsFalseOnSilent()
	{
		var (failing, _) = Create("{}", statusCode: 500);
		var act = async () => await new IndexMappingPatchStep().ExecuteAsync(failing);
		await act.Should().ThrowAsync<Exception>().WithMessage("*mapping of `idx`*");

		var (silent, _) = Create("{}", statusCode: 500, method: BootstrapMethod.Silent);
		(await new IndexMappingPatchStep().ExecuteAsync(silent)).Should().BeFalse();
	}

	[Test]
	public async Task ConflictsAreExposedOnTheContext()
	{
		var (context, invoker) = Create("""{"idx":{"mappings":{"properties":{"reason":{"type":"text"}}}}}""");

		await new IndexMappingPatchStep().ExecuteAsync(context);

		context.MappingConflicts.Should().ContainSingle().Which.Should().StartWith("reason:");
		invoker.Calls.Single(c => c.Method == "PUT").Body.Should().Be("""{"properties":{"reasons":{"type":"keyword"}}}""");
	}

	[Test]
	public void IndexStrategyIncludesThePatchStepByDefaultAndCanOptOut()
	{
		var on = BootstrapStrategies.Index().Should().BeOfType<DefaultBootstrapStrategy>().Subject;
		on.Steps.Select(s => s.GetType()).Should().Equal(typeof(ComponentTemplateStep), typeof(IndexTemplateStep), typeof(IndexMappingPatchStep));

		var off = BootstrapStrategies.Index(patchExistingMappings: false).Should().BeOfType<DefaultBootstrapStrategy>().Subject;
		off.Steps.Select(s => s.GetType()).Should().Equal(typeof(ComponentTemplateStep), typeof(IndexTemplateStep));
	}

	[Test]
	public void PatchStepMustFollowTheComponentTemplateStep()
	{
		var act = () => new DefaultBootstrapStrategy(new IndexMappingPatchStep(), new ComponentTemplateStep());
		act.Should().Throw<ArgumentException>().WithMessage("*IndexMappingPatchStep*");
	}
}
