// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information
using System;
using Elastic.Channels;
using Elastic.Ingest.Apm.Model;
using Elastic.Ingest.Transport;
using Elastic.Transport;

namespace Elastic.Ingest.Apm
{
	public class ApmResponseItemsChannelOptions : TransportResponseItemsChannelOptionsBase<IIntakeObject, EventIntakeResponse, IntakeErrorItem, ApmBufferOptions>
	{
		public ApmResponseItemsChannelOptions(HttpTransport transport) : base(transport) { }
	}


	public class ApmBufferOptions : BufferOptions<IIntakeObject>
	{
	}
}
