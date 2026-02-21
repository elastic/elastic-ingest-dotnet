// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Analysis.Definitions;

namespace Elastic.Mapping.Analysis.Builders;

/// <summary>Builder for selecting and configuring a normalizer type.</summary>
public sealed class NormalizerBuilder
{
	private INormalizerDefinition? _definition;

	/// <summary>Creates a custom normalizer with configurable filters and char filters.</summary>
	public CustomNormalizerBuilder Custom() => new(this);

	/// <summary>Creates a lowercase normalizer.</summary>
	public NormalizerBuilder Lowercase()
	{
		_definition = new LowercaseNormalizerDefinition();
		return this;
	}

	internal void SetDefinition(INormalizerDefinition definition) => _definition = definition;

	internal INormalizerDefinition GetDefinition() =>
		_definition ?? throw new InvalidOperationException("No normalizer type was selected. Call Custom() or Lowercase().");
}

/// <summary>Builder for custom normalizers.</summary>
public sealed class CustomNormalizerBuilder
{
	private readonly NormalizerBuilder _parent;
	private readonly List<string> _filters = [];
	private readonly List<string> _charFilters = [];

	internal CustomNormalizerBuilder(NormalizerBuilder parent) => _parent = parent;

	/// <summary>Adds a single filter to the normalizer.</summary>
	public CustomNormalizerBuilder Filter(string filter)
	{
		_filters.Add(filter);
		return this;
	}

	/// <summary>Adds multiple filters to the normalizer.</summary>
	public CustomNormalizerBuilder Filters(params string[] filters)
	{
		_filters.AddRange(filters);
		return this;
	}

	/// <summary>Adds a single char filter to the normalizer.</summary>
	public CustomNormalizerBuilder CharFilter(string charFilter)
	{
		_charFilters.Add(charFilter);
		return this;
	}

	/// <summary>Adds multiple char filters to the normalizer.</summary>
	public CustomNormalizerBuilder CharFilters(params string[] charFilters)
	{
		_charFilters.AddRange(charFilters);
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator NormalizerBuilder(CustomNormalizerBuilder builder)
	{
		builder._parent.SetDefinition(new CustomNormalizerDefinition(
			builder._filters.ToList(),
			builder._charFilters.ToList()
		));
		return builder._parent;
	}
}
