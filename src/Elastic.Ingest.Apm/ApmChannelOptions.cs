// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Ingest.Apm.Model;
using Elastic.Ingest.Transport;
using Elastic.Transport;

namespace Elastic.Ingest.Apm;

/// <summary>
/// Channel options for <see cref="ApmChannel"/>
/// </summary>
public class ApmChannelOptions : TransportChannelOptionsBase<IIntakeObject, EventIntakeResponse, IntakeErrorItem>
{
	/// <inheritdoc cref="ApmChannelOptions"/>
	public ApmChannelOptions(ITransport transport) : base(transport) { }
}
