// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping.Tests;

public class AccessorDelegateTests
{
	[Test]
	public void GetId_WithIdAttribute_ReturnsValue()
	{
		var context = TestMappingContext.SimpleDocument.Context;
		var doc = new SimpleDocument { Name = "test-id" };

		var result = context.GetId!(doc);

		result.Should().Be("test-id");
	}

	[Test]
	public void GetId_WithNullProperty_ReturnsNull()
	{
		var context = TestMappingContext.AdvancedDocument.Context;
		var doc = new AdvancedDocument { Title = null! };

		var result = context.GetId!(doc);

		result.Should().BeNull();
	}

	[Test]
	public void GetId_WhenNoIdAttribute_DelegateIsNull()
	{
		var context = TestMappingContext.LogEntry.Context;

		context.GetId.Should().BeNull();
	}

	[Test]
	public void GetContentHash_WithContentHashAttribute_ReturnsValue()
	{
		var context = TestMappingContext.AdvancedDocument.Context;
		var doc = new AdvancedDocument { Title = "t", Hash = "abc123" };

		var result = context.GetContentHash!(doc);

		result.Should().Be("abc123");
	}

	[Test]
	public void GetContentHash_WhenNoAttribute_DelegateIsNull()
	{
		var context = TestMappingContext.SimpleDocument.Context;

		context.GetContentHash.Should().BeNull();
	}

	[Test]
	public void GetTimestamp_WithDateTimeProperty_ConvertsToDateTimeOffset()
	{
		var context = TestMappingContext.LogEntry.Context;
		var timestamp = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc);
		var doc = new LogEntry { Timestamp = timestamp };

		var result = context.GetTimestamp!(doc);

		result.Should().NotBeNull();
		result!.Value.DateTime.Should().Be(timestamp);
	}

	[Test]
	public void GetTimestamp_WithDateTimeOffsetProperty_ReturnsDirect()
	{
		var context = ExtendedTestMappingContext.RollingIndex.Context;
		var expected = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);
		var doc = new RollingIndex { Name = "test", EventTime = expected };

		var result = context.GetTimestamp!(doc);

		result.Should().Be(expected);
	}

	[Test]
	public void GetTimestamp_WhenNoAttribute_DelegateIsNull()
	{
		var context = TestMappingContext.SimpleDocument.Context;

		context.GetTimestamp.Should().BeNull();
	}

	[Test]
	public void MappedType_ReturnsCorrectClrType()
	{
		TestMappingContext.LogEntry.Context.MappedType.Should().Be<LogEntry>();
		TestMappingContext.SimpleDocument.Context.MappedType.Should().Be<SimpleDocument>();
		TestMappingContext.AdvancedDocument.Context.MappedType.Should().Be<AdvancedDocument>();
		TestMappingContext.NginxAccessLog.Context.MappedType.Should().Be<NginxAccessLog>();
	}
}
