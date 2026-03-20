// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Threading.Tasks;
using FluentAssertions;

namespace Elastic.Clients.AgentBuilder.IntegrationTests;

public class PluginTests : AgentBuilderTestBase
{
	[Test]
	public async Task CanListPlugins()
	{
		var response = await Client.ListPluginsAsync();
		response.Should().NotBeNull();
		response.Results.Should().NotBeNull();
	}
}
