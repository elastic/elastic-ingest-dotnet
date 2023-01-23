// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Elasticsearch.Managed;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Transport;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Elastic.Ingest.Elasticsearch.IntegrationTests
{
	public class DataStreamIngestionTests : IntegrationTestBase
	{
		public DataStreamIngestionTests(IngestionCluster cluster, ITestOutputHelper output) : base(cluster, output)
		{
		}

		[Fact]
		public async Task EnsureDocumentsEndUpInDataStream()
		{
			// logs-* will use data streams by default in Elasticsearch.
			var targetDataStream = new DataStreamName("logs", "dotnet");
			var slim = new CountdownEvent(1);
			var options = new DataStreamChannelOptions<TimeSeriesDocument>(Client.Transport)
			{
				DataStream = targetDataStream,
				BufferOptions = new ElasticsearchBufferOptions<TimeSeriesDocument>
				{
					WaitHandle = slim, MaxConsumerBufferSize = 1,
				}
			};
			var ecsChannel = new DataStreamChannel<TimeSeriesDocument>(options);

			var dataStream =
				await Client.Indices.GetDataStreamAsync(new GetDataStreamRequest(targetDataStream.ToString()));
			dataStream.DataStreams.Should().BeNullOrEmpty();

			ecsChannel.TryWrite(new TimeSeriesDocument { Timestamp = DateTimeOffset.Now, Message = "hello-world" });
			if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
				throw new Exception("ecs document was not persisted within 10 seconds");

			var refreshResult = await Client.Indices.RefreshAsync(targetDataStream.ToString());
			refreshResult.IsValidResponse.Should().BeTrue("{0}", refreshResult.DebugInformation);
			var searchResult = await Client.SearchAsync<TimeSeriesDocument>(s => s.Indices(targetDataStream.ToString()));
			searchResult.Total.Should().Be(1);

			var storedDocument = searchResult.Documents.First();
			storedDocument.Message.Should().Be("hello-world");

			var hit = searchResult.Hits.First();
			hit.Index.Should().StartWith($".ds-{targetDataStream}-");

			// the following throws in the 8.0.4 version of the client
			// The JSON value could not be converted to Elastic.Clients.Elasticsearch.HealthStatus. Path: $.data_stre...
			// await Client.Indices.GetDataStreamAsync(new GetDataStreamRequest(targetDataStream.ToString())
			var getDataStream =
				await Client.Transport.RequestAsync<StringResponse>(HttpMethod.GET, $"/_data_stream/{targetDataStream}");

			getDataStream.ApiCallDetails.HttpStatusCode.Should()
				.Be(200, "{0}", getDataStream.ApiCallDetails.DebugInformation);

		}
	}
}
