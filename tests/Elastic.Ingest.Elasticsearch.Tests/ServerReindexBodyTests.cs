// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Text.Json;
using Elastic.Ingest.Elasticsearch.Helpers;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests;

public class ServerReindexBodyTests
{
	[Test]
	public void BuildBodyWithoutRemoteProducesLocalReindex()
	{
		var reindex = new ServerReindex(TestSetup.SharedTransport, new ServerReindexOptions
		{
			Source = "src-index",
			Destination = "dst-index",
		});

		var body = reindex.BuildBody();
		var doc = JsonDocument.Parse(body);
		var source = doc.RootElement.GetProperty("source");

		source.GetProperty("index").GetString().Should().Be("src-index");
		source.TryGetProperty("remote", out _).Should().BeFalse("local reindex should not have a remote block");

		doc.RootElement.GetProperty("dest").GetProperty("index").GetString().Should().Be("dst-index");
	}

	[Test]
	public void BuildBodyWithRemoteBasicAuthIncludesCredentials()
	{
		var reindex = new ServerReindex(TestSetup.SharedTransport, new ServerReindexOptions
		{
			Source = "remote-index",
			Destination = "local-index",
			Remote = new RemoteSource
			{
				Host = "https://remote.es.cloud:443",
				Username = "reindex_user",
				Password = "s3cret",
			},
		});

		var body = reindex.BuildBody();
		var doc = JsonDocument.Parse(body);
		var remote = doc.RootElement.GetProperty("source").GetProperty("remote");

		remote.GetProperty("host").GetString().Should().Be("https://remote.es.cloud:443");
		remote.GetProperty("username").GetString().Should().Be("reindex_user");
		remote.GetProperty("password").GetString().Should().Be("s3cret");
		remote.TryGetProperty("api_key", out _).Should().BeFalse();
		remote.TryGetProperty("headers", out _).Should().BeFalse("basic auth should not set headers");
	}

	[Test]
	public void BuildBodyWithRemoteApiKeyUsesNativeField()
	{
		var reindex = new ServerReindex(TestSetup.SharedTransport, new ServerReindexOptions
		{
			Source = "remote-index",
			Destination = "local-index",
			Remote = new RemoteSource
			{
				Host = "https://remote.es.cloud:443",
				ApiKey = "dGVzdEtleQ==",
			},
		});

		var body = reindex.BuildBody();
		var doc = JsonDocument.Parse(body);
		var remote = doc.RootElement.GetProperty("source").GetProperty("remote");

		remote.GetProperty("host").GetString().Should().Be("https://remote.es.cloud:443");
		remote.GetProperty("api_key").GetString().Should().Be("dGVzdEtleQ==");
		remote.TryGetProperty("username", out _).Should().BeFalse();
		remote.TryGetProperty("headers", out _).Should().BeFalse("native api_key should not set headers");
	}

	[Test]
	public void BuildBodyWithRemoteHeadersUsesHeadersBlock()
	{
		var reindex = new ServerReindex(TestSetup.SharedTransport, new ServerReindexOptions
		{
			Source = "remote-index",
			Destination = "local-index",
			Remote = new RemoteSource
			{
				Host = "https://serverless.es.cloud:443",
				Headers = new Dictionary<string, string>
				{
					["Authorization"] = "ApiKey dGVzdEtleQ=="
				},
			},
		});

		var body = reindex.BuildBody();
		var doc = JsonDocument.Parse(body);
		var remote = doc.RootElement.GetProperty("source").GetProperty("remote");

		remote.GetProperty("headers").GetProperty("Authorization").GetString()
			.Should().Be("ApiKey dGVzdEtleQ==");
		remote.TryGetProperty("api_key", out _).Should().BeFalse("headers auth should not set native api_key");
	}

	[Test]
	public void BuildBodyWithRemoteTimeoutsIncludesTimeoutFields()
	{
		var reindex = new ServerReindex(TestSetup.SharedTransport, new ServerReindexOptions
		{
			Source = "remote-index",
			Destination = "local-index",
			Remote = new RemoteSource
			{
				Host = "https://remote.es.cloud:443",
				Username = "user",
				Password = "pass",
				SocketTimeout = "2m",
				ConnectTimeout = "30s",
			},
		});

		var body = reindex.BuildBody();
		var doc = JsonDocument.Parse(body);
		var remote = doc.RootElement.GetProperty("source").GetProperty("remote");

		remote.GetProperty("socket_timeout").GetString().Should().Be("2m");
		remote.GetProperty("connect_timeout").GetString().Should().Be("30s");
	}

	[Test]
	public void BuildBodyWithRemoteAndQueryIncludesBoth()
	{
		var reindex = new ServerReindex(TestSetup.SharedTransport, new ServerReindexOptions
		{
			Source = "remote-index",
			Destination = "local-index",
			Query = """{"match_all":{}}""",
			Remote = new RemoteSource
			{
				Host = "https://remote.es.cloud:443",
				Username = "user",
				Password = "pass",
			},
		});

		var body = reindex.BuildBody();
		var doc = JsonDocument.Parse(body);
		var source = doc.RootElement.GetProperty("source");

		source.TryGetProperty("remote", out _).Should().BeTrue();
		source.TryGetProperty("query", out _).Should().BeTrue();
	}

	[Test]
	public void BuildBodyWithSourceSizeIncludesSizeInSource()
	{
		var reindex = new ServerReindex(TestSetup.SharedTransport, new ServerReindexOptions
		{
			Source = "remote-index",
			Destination = "local-index",
			SourceSize = 10,
			Remote = new RemoteSource
			{
				Host = "https://remote.es.cloud:443",
				Username = "user",
				Password = "pass",
			},
		});

		var body = reindex.BuildBody();
		var doc = JsonDocument.Parse(body);
		var source = doc.RootElement.GetProperty("source");

		source.GetProperty("size").GetInt32().Should().Be(10);
	}

	[Test]
	public void BuildBodyWithConflictsIncludesTopLevelField()
	{
		var reindex = new ServerReindex(TestSetup.SharedTransport, new ServerReindexOptions
		{
			Source = "src-index",
			Destination = "dst-index",
			Conflicts = "proceed",
		});

		var body = reindex.BuildBody();
		var doc = JsonDocument.Parse(body);

		doc.RootElement.GetProperty("conflicts").GetString().Should().Be("proceed");
		doc.RootElement.GetProperty("source").GetProperty("index").GetString().Should().Be("src-index");
	}

	[Test]
	public void BuildBodyWithExcludeInferenceFieldsAddsSourceExclusion()
	{
		var reindex = new ServerReindex(TestSetup.SharedTransport, new ServerReindexOptions
		{
			Source = "semantic-index",
			Destination = "local-index",
			ExcludeInferenceFields = true,
			Remote = new RemoteSource
			{
				Host = "https://remote.es.cloud:443",
				Username = "user",
				Password = "pass",
			},
		});

		var body = reindex.BuildBody();
		var doc = JsonDocument.Parse(body);
		var source = doc.RootElement.GetProperty("source");

		var sourceFilter = source.GetProperty("_source");
		var excludes = sourceFilter.GetProperty("excludes");
		excludes.GetArrayLength().Should().Be(1);
		excludes[0].GetString().Should().Be("_inference_fields");
	}

	[Test]
	public void BuildBodyWithExcludeInferenceFieldsComposesWithScript()
	{
		var reindex = new ServerReindex(TestSetup.SharedTransport, new ServerReindexOptions
		{
			Source = "semantic-index",
			Destination = "local-index",
			ExcludeInferenceFields = true,
			Script = """{"lang":"painless","source":"ctx._source.tag = 'migrated'"}""",
			Remote = new RemoteSource
			{
				Host = "https://remote.es.cloud:443",
				Username = "user",
				Password = "pass",
			},
		});

		var body = reindex.BuildBody();
		var doc = JsonDocument.Parse(body);

		doc.RootElement.GetProperty("source").GetProperty("_source")
			.GetProperty("excludes")[0].GetString().Should().Be("_inference_fields");
		doc.RootElement.GetProperty("script").GetProperty("source").GetString()
			.Should().Contain("migrated");
	}

	[Test]
	public void BuildBodyWithScriptIncludesTopLevelScriptBlock()
	{
		var reindex = new ServerReindex(TestSetup.SharedTransport, new ServerReindexOptions
		{
			Source = "src-index",
			Destination = "dst-index",
			Script = """{"source":"ctx._source.tag = 'migrated'"}""",
		});

		var body = reindex.BuildBody();
		var doc = JsonDocument.Parse(body);

		doc.RootElement.GetProperty("script").GetProperty("source").GetString()
			.Should().Be("ctx._source.tag = 'migrated'");
	}

	[Test]
	public void BuildBodyWithBodyOverrideIgnoresAllStructuredOptions()
	{
		var reindex = new ServerReindex(TestSetup.SharedTransport, new ServerReindexOptions
		{
			Body = """{"source":{"index":"custom"},"dest":{"index":"custom-dest"}}""",
			Remote = new RemoteSource
			{
				Host = "https://remote.es.cloud:443",
			},
			Conflicts = "proceed",
			SourceSize = 5,
			ExcludeInferenceFields = true,
		});

		var body = reindex.BuildBody();
		body.Should().Be("""{"source":{"index":"custom"},"dest":{"index":"custom-dest"}}""",
			"Body override should be used verbatim");
	}
}
