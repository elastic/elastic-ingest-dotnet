// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Text;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Tests;

public abstract class ChannelTestWithSingleDocResponseBase
{
	protected ChannelTestWithSingleDocResponseBase(string url = "https://localhost:9200") =>
		Transport = new DistributedTransport<TransportConfiguration>(
			new TransportConfiguration(new SingleNodePool(new Uri(url)),
					new InMemoryRequestInvoker(Encoding.UTF8.GetBytes("{\"items\":[{\"create\":{\"status\":201}}]}")))
				.DisablePing()
				.EnableDebugMode());

	protected ITransport Transport { get; }
}
