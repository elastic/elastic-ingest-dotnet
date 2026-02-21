// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

namespace Elastic.Mapping.Tests;

public class StjIntegrationTests
{
	[Test]
	public void JsonContext_SnakeCase_FieldNamesFollowPolicy()
	{
		var fields = StjTestMappingContext.SnakeCaseDocument.Fields;

		fields.FirstName.Should().Be("first_name");
		fields.LastName.Should().Be("last_name");
		fields.PageCount.Should().Be("page_count");
		fields.CreatedAt.Should().Be("created_at");
	}

	[Test]
	public void JsonContext_SnakeCase_MappingJsonUsesSnakeCase()
	{
		var json = StjTestMappingContext.SnakeCaseDocument.GetMappingJson();

		json.Should().Contain("\"first_name\"");
		json.Should().Contain("\"last_name\"");
		json.Should().Contain("\"page_count\"");
		json.Should().Contain("\"created_at\"");

		// Should NOT contain PascalCase property names as field names
		json.Should().NotContain("\"FirstName\"");
		json.Should().NotContain("\"LastName\"");
	}

	[Test]
	public void JsonContext_SnakeCase_FieldMappingDictionary_UsesSnakeCase()
	{
		var propertyToField = StjTestMappingContext.SnakeCaseDocument.FieldMapping.PropertyToField;

		propertyToField["FirstName"].Should().Be("first_name");
		propertyToField["LastName"].Should().Be("last_name");
		propertyToField["PageCount"].Should().Be("page_count");
		propertyToField["CreatedAt"].Should().Be("created_at");
	}

	[Test]
	public void JsonContext_SnakeCase_FieldToProperty_ReverseMapping()
	{
		var fieldToProperty = StjTestMappingContext.SnakeCaseDocument.FieldMapping.FieldToProperty;

		fieldToProperty["first_name"].Should().Be("FirstName");
		fieldToProperty["last_name"].Should().Be("LastName");
		fieldToProperty["page_count"].Should().Be("PageCount");
		fieldToProperty["created_at"].Should().Be("CreatedAt");
	}
}
