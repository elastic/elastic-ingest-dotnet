// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json;

namespace Elastic.Mapping.Tests;

/// <summary>
/// Validates that CLR types are correctly inferred to Elasticsearch field types
/// when no Elastic.Mapping field attributes ([Text], [Keyword], [Date], etc.) are present.
/// </summary>
public class ClrTypeInferenceTests
{
	private JsonElement _properties;

	[Before(Test)]
	public void Setup()
	{
		var json = ClrInferenceMappingContext.ClrInferenceDocument.GetMappingJson();
		using var doc = JsonDocument.Parse(json);
		_properties = doc.RootElement.GetProperty("properties").Clone();
	}

	[Test]
	public void String_InfersAsText()
	{
		_properties.GetProperty("stringField").GetProperty("type").GetString().Should().Be("text");
		_properties.GetProperty("nullableStringField").GetProperty("type").GetString().Should().Be("text");
	}

	[Test]
	public void StringArray_InfersAsText()
	{
		_properties.GetProperty("stringArrayField").GetProperty("type").GetString().Should().Be("text");
		_properties.GetProperty("stringListField").GetProperty("type").GetString().Should().Be("text");
	}

	[Test]
	public void Int_InfersAsInteger()
	{
		_properties.GetProperty("intField").GetProperty("type").GetString().Should().Be("integer");
		_properties.GetProperty("nullableIntField").GetProperty("type").GetString().Should().Be("integer");
	}

	[Test]
	public void Long_InfersAsLong()
	{
		_properties.GetProperty("longField").GetProperty("type").GetString().Should().Be("long");
		_properties.GetProperty("nullableLongField").GetProperty("type").GetString().Should().Be("long");
	}

	[Test]
	public void Short_InfersAsShort()
	{
		_properties.GetProperty("shortField").GetProperty("type").GetString().Should().Be("short");
		_properties.GetProperty("nullableShortField").GetProperty("type").GetString().Should().Be("short");
	}

	[Test]
	public void Byte_InfersAsByte()
	{
		_properties.GetProperty("byteField").GetProperty("type").GetString().Should().Be("byte");
		_properties.GetProperty("nullableByteField").GetProperty("type").GetString().Should().Be("byte");
	}

	[Test]
	public void Double_InfersAsDouble()
	{
		_properties.GetProperty("doubleField").GetProperty("type").GetString().Should().Be("double");
		_properties.GetProperty("nullableDoubleField").GetProperty("type").GetString().Should().Be("double");
	}

	[Test]
	public void Float_InfersAsFloat()
	{
		_properties.GetProperty("floatField").GetProperty("type").GetString().Should().Be("float");
		_properties.GetProperty("nullableFloatField").GetProperty("type").GetString().Should().Be("float");
	}

	[Test]
	public void Decimal_InfersAsDouble()
	{
		_properties.GetProperty("decimalField").GetProperty("type").GetString().Should().Be("double");
		_properties.GetProperty("nullableDecimalField").GetProperty("type").GetString().Should().Be("double");
	}

	[Test]
	public void Bool_InfersAsBoolean()
	{
		_properties.GetProperty("boolField").GetProperty("type").GetString().Should().Be("boolean");
		_properties.GetProperty("nullableBoolField").GetProperty("type").GetString().Should().Be("boolean");
	}

	[Test]
	public void DateTime_InfersAsDate()
	{
		_properties.GetProperty("dateTimeField").GetProperty("type").GetString().Should().Be("date");
		_properties.GetProperty("nullableDateTimeField").GetProperty("type").GetString().Should().Be("date");
	}

	[Test]
	public void DateTimeOffset_InfersAsDate()
	{
		_properties.GetProperty("dateTimeOffsetField").GetProperty("type").GetString().Should().Be("date");
		_properties.GetProperty("nullableDateTimeOffsetField").GetProperty("type").GetString().Should().Be("date");
	}

	[Test]
	public void Guid_InfersAsKeyword()
	{
		_properties.GetProperty("guidField").GetProperty("type").GetString().Should().Be("keyword");
		_properties.GetProperty("nullableGuidField").GetProperty("type").GetString().Should().Be("keyword");
	}

	[Test]
	public void Enum_InfersAsKeyword()
	{
		_properties.GetProperty("statusField").GetProperty("type").GetString().Should().Be("keyword");
		_properties.GetProperty("nullableStatusField").GetProperty("type").GetString().Should().Be("keyword");
	}

	[Test]
	public void NestedObject_InfersAsObject()
	{
		_properties.GetProperty("addressField").GetProperty("type").GetString().Should().Be("object");
	}

	[Test]
	public void NestedObjectList_InfersAsObject()
	{
		_properties.GetProperty("addressListField").GetProperty("type").GetString().Should().Be("object");
	}

	[Test]
	public void NestedObject_IncludesSubProperties()
	{
		var address = _properties.GetProperty("addressField");
		address.GetProperty("properties").GetProperty("street").GetProperty("type").GetString().Should().Be("text");
		address.GetProperty("properties").GetProperty("city").GetProperty("type").GetString().Should().Be("text");
	}
}
