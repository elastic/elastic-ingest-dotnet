// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Mappings.Definitions;

namespace Elastic.Mapping.Mappings.Builders;

/// <summary>Builder for configuring dynamic templates.</summary>
public sealed class DynamicTemplateBuilder
{
	private readonly string _name;
	private string? _match;
	private string? _unmatch;
	private string? _pathMatch;
	private string? _pathUnmatch;
	private string? _matchMappingType;
	private string? _matchPattern;
	private IFieldDefinition? _mapping;

	internal DynamicTemplateBuilder(string name) => _name = name;

	/// <summary>Sets the match pattern for field names.</summary>
	public DynamicTemplateBuilder Match(string match)
	{
		_match = match;
		return this;
	}

	/// <summary>Sets the unmatch pattern for field names.</summary>
	public DynamicTemplateBuilder Unmatch(string unmatch)
	{
		_unmatch = unmatch;
		return this;
	}

	/// <summary>Sets the path_match pattern for dotted field paths.</summary>
	public DynamicTemplateBuilder PathMatch(string pathMatch)
	{
		_pathMatch = pathMatch;
		return this;
	}

	/// <summary>Sets the path_unmatch pattern for dotted field paths.</summary>
	public DynamicTemplateBuilder PathUnmatch(string pathUnmatch)
	{
		_pathUnmatch = pathUnmatch;
		return this;
	}

	/// <summary>Sets the match_mapping_type for data type matching.</summary>
	public DynamicTemplateBuilder MatchMappingType(string mappingType)
	{
		_matchMappingType = mappingType;
		return this;
	}

	/// <summary>Sets the match_pattern (regex or simple).</summary>
	public DynamicTemplateBuilder MatchPattern(string matchPattern)
	{
		_matchPattern = matchPattern;
		return this;
	}

	/// <summary>Sets the mapping to apply to matched fields.</summary>
	public DynamicTemplateBuilder Mapping(Func<FieldBuilder, FieldBuilder> configure)
	{
		var builder = new FieldBuilder();
		_ = configure(builder);
		_mapping = builder.GetDefinition();
		return this;
	}

	internal DynamicTemplateDefinition GetDefinition() =>
		new(
			_name,
			_match,
			_unmatch,
			_pathMatch,
			_pathUnmatch,
			_matchMappingType,
			_matchPattern,
			_mapping
		);
}
