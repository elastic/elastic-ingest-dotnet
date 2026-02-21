// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Analysis.Definitions;

namespace Elastic.Mapping.Analysis.Builders;

/// <summary>Builder for selecting and configuring a tokenizer type.</summary>
public sealed class TokenizerBuilder
{
	private ITokenizerDefinition? _definition;

	/// <summary>Creates a pattern tokenizer.</summary>
	public PatternTokenizerBuilder Pattern() => new(this);

	/// <summary>Creates a char_group tokenizer.</summary>
	public CharGroupTokenizerBuilder CharGroup() => new(this);

	/// <summary>Creates an edge_ngram tokenizer.</summary>
	public EdgeNGramTokenizerBuilder EdgeNGram() => new(this);

	/// <summary>Creates an ngram tokenizer.</summary>
	public NGramTokenizerBuilder NGram() => new(this);

	/// <summary>Creates a path_hierarchy tokenizer.</summary>
	public PathHierarchyTokenizerBuilder PathHierarchy() => new(this);

	/// <summary>Creates a uax_url_email tokenizer.</summary>
	public UaxUrlEmailTokenizerBuilder UaxUrlEmail() => new(this);

	/// <summary>Creates a keyword tokenizer.</summary>
	public KeywordTokenizerBuilder Keyword() => new(this);

	/// <summary>Creates a standard tokenizer.</summary>
	public StandardTokenizerBuilder Standard() => new(this);

	/// <summary>Creates a whitespace tokenizer.</summary>
	public WhitespaceTokenizerBuilder Whitespace() => new(this);

	/// <summary>Creates a simple_pattern tokenizer.</summary>
	public TokenizerBuilder SimplePattern(string pattern)
	{
		_definition = new SimplePatternTokenizerDefinition(pattern);
		return this;
	}

	/// <summary>Creates a simple_pattern_split tokenizer.</summary>
	public TokenizerBuilder SimplePatternSplit(string pattern)
	{
		_definition = new SimplePatternSplitTokenizerDefinition(pattern);
		return this;
	}

	internal void SetDefinition(ITokenizerDefinition definition) => _definition = definition;

	internal ITokenizerDefinition GetDefinition() =>
		_definition ?? throw new InvalidOperationException("No tokenizer type was selected. Call Pattern(), EdgeNGram(), Standard(), etc.");
}

/// <summary>Builder for pattern tokenizers.</summary>
public sealed class PatternTokenizerBuilder
{
	private readonly TokenizerBuilder _parent;
	private string _pattern = "\\W+";
	private string? _flags;
	private int _group;

	internal PatternTokenizerBuilder(TokenizerBuilder parent) => _parent = parent;

	/// <summary>Sets the pattern for tokenization.</summary>
	public PatternTokenizerBuilder PatternValue(string pattern)
	{
		_pattern = pattern;
		return this;
	}

	/// <summary>Sets the regex flags.</summary>
	public PatternTokenizerBuilder Flags(string flags)
	{
		_flags = flags;
		return this;
	}

	/// <summary>Sets the capture group to use.</summary>
	public PatternTokenizerBuilder Group(int group)
	{
		_group = group;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenizerBuilder(PatternTokenizerBuilder builder)
	{
		builder._parent.SetDefinition(new PatternTokenizerDefinition(
			builder._pattern,
			builder._flags,
			builder._group
		));
		return builder._parent;
	}
}

/// <summary>Builder for char_group tokenizers.</summary>
public sealed class CharGroupTokenizerBuilder
{
	private readonly TokenizerBuilder _parent;
	private readonly List<string> _tokenizeOnChars = [];
	private int? _maxTokenLength;

	internal CharGroupTokenizerBuilder(TokenizerBuilder parent) => _parent = parent;

	/// <summary>Adds characters to tokenize on.</summary>
	public CharGroupTokenizerBuilder TokenizeOnChars(params string[] chars)
	{
		_tokenizeOnChars.AddRange(chars);
		return this;
	}

	/// <summary>Sets the maximum token length.</summary>
	public CharGroupTokenizerBuilder MaxTokenLength(int maxTokenLength)
	{
		_maxTokenLength = maxTokenLength;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenizerBuilder(CharGroupTokenizerBuilder builder)
	{
		builder._parent.SetDefinition(new CharGroupTokenizerDefinition(
			builder._tokenizeOnChars.ToList(),
			builder._maxTokenLength
		));
		return builder._parent;
	}
}

/// <summary>Builder for edge_ngram tokenizers.</summary>
public sealed class EdgeNGramTokenizerBuilder
{
	private readonly TokenizerBuilder _parent;
	private int _minGram = 1;
	private int _maxGram = 2;
	private List<string>? _tokenChars;

	internal EdgeNGramTokenizerBuilder(TokenizerBuilder parent) => _parent = parent;

	/// <summary>Sets the minimum gram size.</summary>
	public EdgeNGramTokenizerBuilder MinGram(int minGram)
	{
		_minGram = minGram;
		return this;
	}

	/// <summary>Sets the maximum gram size.</summary>
	public EdgeNGramTokenizerBuilder MaxGram(int maxGram)
	{
		_maxGram = maxGram;
		return this;
	}

	/// <summary>Sets the token character classes to include.</summary>
	public EdgeNGramTokenizerBuilder TokenChars(params string[] tokenChars)
	{
		_tokenChars = [.. tokenChars];
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenizerBuilder(EdgeNGramTokenizerBuilder builder)
	{
		builder._parent.SetDefinition(new EdgeNGramTokenizerDefinition(
			builder._minGram,
			builder._maxGram,
			builder._tokenChars
		));
		return builder._parent;
	}
}

/// <summary>Builder for ngram tokenizers.</summary>
public sealed class NGramTokenizerBuilder
{
	private readonly TokenizerBuilder _parent;
	private int _minGram = 1;
	private int _maxGram = 2;
	private List<string>? _tokenChars;

	internal NGramTokenizerBuilder(TokenizerBuilder parent) => _parent = parent;

	/// <summary>Sets the minimum gram size.</summary>
	public NGramTokenizerBuilder MinGram(int minGram)
	{
		_minGram = minGram;
		return this;
	}

	/// <summary>Sets the maximum gram size.</summary>
	public NGramTokenizerBuilder MaxGram(int maxGram)
	{
		_maxGram = maxGram;
		return this;
	}

	/// <summary>Sets the token character classes to include.</summary>
	public NGramTokenizerBuilder TokenChars(params string[] tokenChars)
	{
		_tokenChars = [.. tokenChars];
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenizerBuilder(NGramTokenizerBuilder builder)
	{
		builder._parent.SetDefinition(new NGramTokenizerDefinition(
			builder._minGram,
			builder._maxGram,
			builder._tokenChars
		));
		return builder._parent;
	}
}

/// <summary>Builder for path_hierarchy tokenizers.</summary>
public sealed class PathHierarchyTokenizerBuilder
{
	private readonly TokenizerBuilder _parent;
	private char _delimiter = '/';
	private char? _replacement;
	private int _skip;
	private bool _reverse;

	internal PathHierarchyTokenizerBuilder(TokenizerBuilder parent) => _parent = parent;

	/// <summary>Sets the delimiter character.</summary>
	public PathHierarchyTokenizerBuilder Delimiter(char delimiter)
	{
		_delimiter = delimiter;
		return this;
	}

	/// <summary>Sets the replacement character.</summary>
	public PathHierarchyTokenizerBuilder Replacement(char replacement)
	{
		_replacement = replacement;
		return this;
	}

	/// <summary>Sets the number of initial tokens to skip.</summary>
	public PathHierarchyTokenizerBuilder Skip(int skip)
	{
		_skip = skip;
		return this;
	}

	/// <summary>Sets whether to reverse the token order.</summary>
	public PathHierarchyTokenizerBuilder Reverse(bool reverse = true)
	{
		_reverse = reverse;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenizerBuilder(PathHierarchyTokenizerBuilder builder)
	{
		builder._parent.SetDefinition(new PathHierarchyTokenizerDefinition(
			builder._delimiter,
			builder._replacement,
			builder._skip,
			builder._reverse
		));
		return builder._parent;
	}
}

/// <summary>Builder for uax_url_email tokenizers.</summary>
public sealed class UaxUrlEmailTokenizerBuilder
{
	private readonly TokenizerBuilder _parent;
	private int? _maxTokenLength;

	internal UaxUrlEmailTokenizerBuilder(TokenizerBuilder parent) => _parent = parent;

	/// <summary>Sets the maximum token length.</summary>
	public UaxUrlEmailTokenizerBuilder MaxTokenLength(int maxTokenLength)
	{
		_maxTokenLength = maxTokenLength;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenizerBuilder(UaxUrlEmailTokenizerBuilder builder)
	{
		builder._parent.SetDefinition(new UaxUrlEmailTokenizerDefinition(builder._maxTokenLength));
		return builder._parent;
	}
}

/// <summary>Builder for keyword tokenizers.</summary>
public sealed class KeywordTokenizerBuilder
{
	private readonly TokenizerBuilder _parent;
	private int? _bufferSize;

	internal KeywordTokenizerBuilder(TokenizerBuilder parent) => _parent = parent;

	/// <summary>Sets the buffer size.</summary>
	public KeywordTokenizerBuilder BufferSize(int bufferSize)
	{
		_bufferSize = bufferSize;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenizerBuilder(KeywordTokenizerBuilder builder)
	{
		builder._parent.SetDefinition(new KeywordTokenizerDefinition(builder._bufferSize));
		return builder._parent;
	}
}

/// <summary>Builder for standard tokenizers.</summary>
public sealed class StandardTokenizerBuilder
{
	private readonly TokenizerBuilder _parent;
	private int? _maxTokenLength;

	internal StandardTokenizerBuilder(TokenizerBuilder parent) => _parent = parent;

	/// <summary>Sets the maximum token length.</summary>
	public StandardTokenizerBuilder MaxTokenLength(int maxTokenLength)
	{
		_maxTokenLength = maxTokenLength;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenizerBuilder(StandardTokenizerBuilder builder)
	{
		builder._parent.SetDefinition(new StandardTokenizerDefinition(builder._maxTokenLength));
		return builder._parent;
	}
}

/// <summary>Builder for whitespace tokenizers.</summary>
public sealed class WhitespaceTokenizerBuilder
{
	private readonly TokenizerBuilder _parent;
	private int? _maxTokenLength;

	internal WhitespaceTokenizerBuilder(TokenizerBuilder parent) => _parent = parent;

	/// <summary>Sets the maximum token length.</summary>
	public WhitespaceTokenizerBuilder MaxTokenLength(int maxTokenLength)
	{
		_maxTokenLength = maxTokenLength;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator TokenizerBuilder(WhitespaceTokenizerBuilder builder)
	{
		builder._parent.SetDefinition(new WhitespaceTokenizerDefinition(builder._maxTokenLength));
		return builder._parent;
	}
}
