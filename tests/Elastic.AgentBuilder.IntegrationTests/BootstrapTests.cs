// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Elastic.AgentBuilder.Tools;
using FluentAssertions;

namespace Elastic.AgentBuilder.IntegrationTests;

public class BootstrapTests : AgentBuilderTestBase
{
	private const string TestToolId = "dotnet-bootstrap-test-tool";

	[Test]
	public async Task Bootstrap_CreatesAndSkipsOnSecondRun()
	{
		try { await Client.DeleteToolAsync(TestToolId); } catch { /* cleanup from previous runs */ }

		var bootstrapper = new AgentBuilderBootstrapper(Client);
		var definition = new BootstrapDefinition
		{
			EsqlTools =
			[
				new CreateEsqlToolRequest
				{
					Id = TestToolId,
					Description = "Bootstrap test tool",
					Configuration = new EsqlToolConfiguration
					{
						Query = "FROM test | LIMIT 1",
						Params = new Dictionary<string, EsqlToolParam>()
					}
				}
			]
		};

		var result1 = await bootstrapper.BootstrapAsync(BootstrapMethod.Failure, definition);
		result1.Should().BeTrue();

		var tool = await Client.GetToolAsync(TestToolId);
		tool.Tags.Should().NotBeNull();
		tool.Tags!.Any(t => t.StartsWith("_hash:", System.StringComparison.Ordinal)).Should().BeTrue();

		var result2 = await bootstrapper.BootstrapAsync(BootstrapMethod.Failure, definition);
		result2.Should().BeTrue();

		await Client.DeleteToolAsync(TestToolId);
	}

	public override void Dispose()
	{
		try { Client.DeleteToolAsync(TestToolId).GetAwaiter().GetResult(); } catch { /* cleanup */ }
		base.Dispose();
	}
}
