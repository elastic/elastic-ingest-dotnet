// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#pragma warning disable CA1720 // Identifier contains type name - intentional, matches Elasticsearch field types

using Elastic.Mapping.Mappings.Definitions;

namespace Elastic.Mapping.Mappings.Builders;

/// <summary>Builder for configuring runtime fields.</summary>
public sealed class RuntimeFieldBuilder
{
	private string? _type;
	private string? _script;

	/// <summary>Sets the runtime field type to keyword.</summary>
	public RuntimeFieldBuilder Keyword()
	{
		_type = "keyword";
		return this;
	}

	/// <summary>Sets the runtime field type to long.</summary>
	public RuntimeFieldBuilder Long()
	{
		_type = "long";
		return this;
	}

	/// <summary>Sets the runtime field type to double.</summary>
	public RuntimeFieldBuilder Double()
	{
		_type = "double";
		return this;
	}

	/// <summary>Sets the runtime field type to date.</summary>
	public RuntimeFieldBuilder Date()
	{
		_type = "date";
		return this;
	}

	/// <summary>Sets the runtime field type to boolean.</summary>
	public RuntimeFieldBuilder Boolean()
	{
		_type = "boolean";
		return this;
	}

	/// <summary>Sets the runtime field type to ip.</summary>
	public RuntimeFieldBuilder Ip()
	{
		_type = "ip";
		return this;
	}

	/// <summary>Sets the runtime field type to geo_point.</summary>
	public RuntimeFieldBuilder GeoPoint()
	{
		_type = "geo_point";
		return this;
	}

	/// <summary>Sets the Painless script for computing the runtime field value.</summary>
	public RuntimeFieldBuilder Script(string script)
	{
		_script = script;
		return this;
	}

	internal RuntimeFieldDefinition GetDefinition() =>
		new(
			_type ?? throw new InvalidOperationException("Runtime field type required. Call Keyword(), Long(), Double(), etc."),
			_script ?? throw new InvalidOperationException("Script required for runtime field.")
		);
}
