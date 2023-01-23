using System;

namespace Elastic.Ingest.IntegrationTests;

public class TestDocument
{
	public DateTimeOffset Timestamp { get; set; }
	public string Message { get; set; }
}
