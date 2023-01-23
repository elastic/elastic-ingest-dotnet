using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Elasticsearch.Xunit;
using Elastic.Elasticsearch.Xunit.XunitPlumbing;
using Elastic.Ingest.Elasticsearch;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Transport;
using FluentAssertions;
using Xunit;

namespace Elastic.Ingest.IntegrationTests
{
	public class DataStreamIngestionTests : IClusterFixture<IngestionCluster>
	{
		private ElasticsearchClient Client { get; }

		public DataStreamIngestionTests(IngestionCluster cluster) =>
			Client = cluster.GetOrAddClient(c =>
			{
				var nodes = cluster.NodesUris();
				var connectionPool = new StaticNodePool(nodes);
				var settings = new ElasticsearchClientSettings(connectionPool)
					.EnableDebugMode();
				return new ElasticsearchClient(settings);
			});


		[Fact]
		public async Task ChannelCanSetupElasticsearchTemplates()
		{
			var targetDataStream = new Elasticsearch.DataStreamName("hello", "world");
			var slim = new CountdownEvent(1);
			var options = new DataStreamChannelOptions<TestDocument>(Client.Transport)
			{
				DataStream = targetDataStream,
				BufferOptions = new ElasticsearchBufferOptions<TestDocument>
				{
					WaitHandle = slim,
					MaxConsumerBufferSize = 1,
				}
			};
			var ecsChannel = new DataStreamChannel<TestDocument>(options);

			var dataStream = await Client.Indices.GetDataStreamAsync(new GetDataStreamRequest(targetDataStream.ToString()));
			dataStream.DataStreams.Should().BeNullOrEmpty();

			ecsChannel.TryWrite(new TestDocument { Timestamp = DateTimeOffset.Now, Message = "hello-world" });
			if (!slim.WaitHandle.WaitOne(TimeSpan.FromSeconds(10)))
					throw new Exception("ecs document was not persisted within 10 seconds");

			// Client error
			// dataStream = await Client.Indices.GetDataStreamAsync(new DataStreamRequest(targetDataStream.ToString()));
			// dataStream.DataStreams.Should().NotBeNullOrEmpty();

		}

	}

}
