// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Analysis.Definitions;

namespace Elastic.Mapping.Analysis.Builders;

/// <summary>Builder for selecting and configuring a char filter type.</summary>
public sealed class CharFilterBuilder
{
	private ICharFilterDefinition? _definition;

	/// <summary>Creates a pattern_replace char filter.</summary>
	public PatternReplaceCharFilterBuilder PatternReplace() => new(this);

	/// <summary>Creates a mapping char filter.</summary>
	public MappingCharFilterBuilder Mapping() => new(this);

	/// <summary>Creates an html_strip char filter.</summary>
	public HtmlStripCharFilterBuilder HtmlStrip() => new(this);

	/// <summary>Creates a kuromoji_iteration_mark char filter.</summary>
	public KuromojiIterationMarkCharFilterBuilder KuromojiIterationMark() => new(this);

	/// <summary>Creates an icu_normalizer char filter.</summary>
	public IcuNormalizerCharFilterBuilder IcuNormalizer() => new(this);

	internal void SetDefinition(ICharFilterDefinition definition) => _definition = definition;

	internal ICharFilterDefinition GetDefinition() =>
		_definition ?? throw new InvalidOperationException("No char filter type was selected. Call PatternReplace(), Mapping(), HtmlStrip(), etc.");
}

/// <summary>Builder for pattern_replace char filters.</summary>
public sealed class PatternReplaceCharFilterBuilder
{
	private readonly CharFilterBuilder _parent;
	private string _pattern = ".*";
	private string _replacement = "";
	private string? _flags;

	internal PatternReplaceCharFilterBuilder(CharFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the pattern.</summary>
	public PatternReplaceCharFilterBuilder Pattern(string pattern)
	{
		_pattern = pattern;
		return this;
	}

	/// <summary>Sets the replacement string.</summary>
	public PatternReplaceCharFilterBuilder Replacement(string replacement)
	{
		_replacement = replacement;
		return this;
	}

	/// <summary>Sets the regex flags.</summary>
	public PatternReplaceCharFilterBuilder Flags(string flags)
	{
		_flags = flags;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator CharFilterBuilder(PatternReplaceCharFilterBuilder builder)
	{
		builder._parent.SetDefinition(new PatternReplaceCharFilterDefinition(
			builder._pattern,
			builder._replacement,
			builder._flags
		));
		return builder._parent;
	}
}

/// <summary>Builder for mapping char filters.</summary>
public sealed class MappingCharFilterBuilder
{
	private readonly CharFilterBuilder _parent;
	private List<string>? _mappings;
	private string? _mappingsPath;

	internal MappingCharFilterBuilder(CharFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the mappings inline.</summary>
	public MappingCharFilterBuilder Mappings(params string[] mappings)
	{
		_mappings = [.. mappings];
		return this;
	}

	/// <summary>Sets the path to the mappings file.</summary>
	public MappingCharFilterBuilder MappingsPath(string path)
	{
		_mappingsPath = path;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator CharFilterBuilder(MappingCharFilterBuilder builder)
	{
		builder._parent.SetDefinition(new MappingCharFilterDefinition(
			builder._mappings,
			builder._mappingsPath
		));
		return builder._parent;
	}
}

/// <summary>Builder for html_strip char filters.</summary>
public sealed class HtmlStripCharFilterBuilder
{
	private readonly CharFilterBuilder _parent;
	private List<string>? _escapedTags;

	internal HtmlStripCharFilterBuilder(CharFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the tags to escape (not strip).</summary>
	public HtmlStripCharFilterBuilder EscapedTags(params string[] tags)
	{
		_escapedTags = [.. tags];
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator CharFilterBuilder(HtmlStripCharFilterBuilder builder)
	{
		builder._parent.SetDefinition(new HtmlStripCharFilterDefinition(builder._escapedTags));
		return builder._parent;
	}
}

/// <summary>Builder for kuromoji_iteration_mark char filters.</summary>
public sealed class KuromojiIterationMarkCharFilterBuilder
{
	private readonly CharFilterBuilder _parent;
	private bool _normalizeKanji = true;
	private bool _normalizeKana = true;

	internal KuromojiIterationMarkCharFilterBuilder(CharFilterBuilder parent) => _parent = parent;

	/// <summary>Sets whether to normalize kanji.</summary>
	public KuromojiIterationMarkCharFilterBuilder NormalizeKanji(bool normalize)
	{
		_normalizeKanji = normalize;
		return this;
	}

	/// <summary>Sets whether to normalize kana.</summary>
	public KuromojiIterationMarkCharFilterBuilder NormalizeKana(bool normalize)
	{
		_normalizeKana = normalize;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator CharFilterBuilder(KuromojiIterationMarkCharFilterBuilder builder)
	{
		builder._parent.SetDefinition(new KuromojiIterationMarkCharFilterDefinition(
			builder._normalizeKanji,
			builder._normalizeKana
		));
		return builder._parent;
	}
}

/// <summary>Builder for icu_normalizer char filters.</summary>
public sealed class IcuNormalizerCharFilterBuilder
{
	private readonly CharFilterBuilder _parent;
	private string _name = "nfkc_cf";
	private string? _mode;

	internal IcuNormalizerCharFilterBuilder(CharFilterBuilder parent) => _parent = parent;

	/// <summary>Sets the normalization form name.</summary>
	public IcuNormalizerCharFilterBuilder Name(string name)
	{
		_name = name;
		return this;
	}

	/// <summary>Sets the mode.</summary>
	public IcuNormalizerCharFilterBuilder Mode(string mode)
	{
		_mode = mode;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator CharFilterBuilder(IcuNormalizerCharFilterBuilder builder)
	{
		builder._parent.SetDefinition(new IcuNormalizerCharFilterDefinition(
			builder._name,
			builder._mode
		));
		return builder._parent;
	}
}
