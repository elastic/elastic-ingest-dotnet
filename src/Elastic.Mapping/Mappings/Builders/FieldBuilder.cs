// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

#pragma warning disable CA1720 // Identifier contains type name - intentional, matches Elasticsearch field types

using Elastic.Mapping.Mappings.Definitions;

namespace Elastic.Mapping.Mappings.Builders;

/// <summary>Builder for selecting and configuring a field type.</summary>
public sealed class FieldBuilder
{
	private IFieldDefinition? _definition;

	/// <summary>Creates a text field.</summary>
	public TextFieldBuilder Text() => new(this);

	/// <summary>Creates a keyword field.</summary>
	public KeywordFieldBuilder Keyword() => new(this);

	/// <summary>Creates a date field.</summary>
	public DateFieldBuilder Date() => new(this);

	/// <summary>Creates a long field.</summary>
	public LongFieldBuilder Long() => new(this);

	/// <summary>Creates an integer field.</summary>
	public IntegerFieldBuilder Integer() => new(this);

	/// <summary>Creates a short field.</summary>
	public ShortFieldBuilder Short() => new(this);

	/// <summary>Creates a byte field.</summary>
	public ByteFieldBuilder Byte() => new(this);

	/// <summary>Creates a double field.</summary>
	public DoubleFieldBuilder Double() => new(this);

	/// <summary>Creates a float field.</summary>
	public FloatFieldBuilder Float() => new(this);

	/// <summary>Creates a boolean field.</summary>
	public BooleanFieldBuilder Boolean() => new(this);

	/// <summary>Creates an IP field.</summary>
	public IpFieldBuilder Ip() => new(this);

	/// <summary>Creates a geo_point field.</summary>
	public GeoPointFieldBuilder GeoPoint() => new(this);

	/// <summary>Creates a geo_shape field.</summary>
	public GeoShapeFieldBuilder GeoShape() => new(this);

	/// <summary>Creates a nested field.</summary>
	public NestedFieldBuilder Nested() => new(this);

	/// <summary>Creates an object field.</summary>
	public ObjectFieldBuilder Object() => new(this);

	/// <summary>Creates a completion field.</summary>
	public CompletionFieldBuilder Completion() => new(this);

	/// <summary>Creates a dense_vector field.</summary>
	public DenseVectorFieldBuilder DenseVector() => new(this);

	/// <summary>Creates a semantic_text field.</summary>
	public SemanticTextFieldBuilder SemanticText() => new(this);

	/// <summary>Creates a search_as_you_type field.</summary>
	public SearchAsYouTypeFieldBuilder SearchAsYouType() => new(this);

	/// <summary>Creates a flattened field.</summary>
	public FlattenedFieldBuilder Flattened() => new(this);

	/// <summary>Creates a binary field.</summary>
	public BinaryFieldBuilder Binary() => new(this);

	/// <summary>Creates a range field.</summary>
	public RangeFieldBuilder Range(string rangeType) => new(this, rangeType);

	/// <summary>Creates an alias field.</summary>
	public FieldBuilder Alias(string path)
	{
		_definition = new AliasFieldDefinition(path);
		return this;
	}

	/// <summary>Creates a percolator field.</summary>
	public FieldBuilder Percolator()
	{
		_definition = new PercolatorFieldDefinition();
		return this;
	}

	/// <summary>Creates a rank_feature field.</summary>
	public RankFeatureFieldBuilder RankFeature() => new(this);

	/// <summary>Creates a rank_features field.</summary>
	public FieldBuilder RankFeatures()
	{
		_definition = new RankFeaturesFieldDefinition();
		return this;
	}

	/// <summary>Creates a histogram field.</summary>
	public FieldBuilder Histogram()
	{
		_definition = new HistogramFieldDefinition();
		return this;
	}

	/// <summary>Creates a constant_keyword field.</summary>
	public ConstantKeywordFieldBuilder ConstantKeyword() => new(this);

	/// <summary>Creates a wildcard field.</summary>
	public WildcardFieldBuilder Wildcard() => new(this);

	/// <summary>Creates a version field.</summary>
	public FieldBuilder Version()
	{
		_definition = new VersionFieldDefinition();
		return this;
	}

	internal void SetDefinition(IFieldDefinition definition) => _definition = definition;

	/// <summary>Gets the field definition. Used by generated code.</summary>
	public IFieldDefinition GetDefinition() =>
		_definition ?? throw new InvalidOperationException("No field type was selected. Call Text(), Keyword(), Date(), etc.");
}

/// <summary>Builder for multi-fields within a field.</summary>
public sealed class MultiFieldBuilder
{
	private IFieldDefinition? _definition;

	/// <summary>Creates a keyword multi-field.</summary>
	public KeywordMultiFieldBuilder Keyword() => new(this);

	/// <summary>Creates a text multi-field.</summary>
	public TextMultiFieldBuilder Text() => new(this);

	/// <summary>Creates a search_as_you_type multi-field.</summary>
	public SearchAsYouTypeMultiFieldBuilder SearchAsYouType() => new(this);

	internal void SetDefinition(IFieldDefinition definition) => _definition = definition;

	/// <summary>Gets the field definition. Used by generated code.</summary>
	public IFieldDefinition GetDefinition() =>
		_definition ?? throw new InvalidOperationException("No multi-field type was selected.");
}

/// <summary>Builder for keyword multi-fields.</summary>
public sealed class KeywordMultiFieldBuilder
{
	private readonly MultiFieldBuilder _parent;
	private string? _normalizer;
	private int? _ignoreAbove;

	internal KeywordMultiFieldBuilder(MultiFieldBuilder parent) => _parent = parent;

	/// <summary>Sets the normalizer.</summary>
	public KeywordMultiFieldBuilder Normalizer(string normalizer)
	{
		_normalizer = normalizer;
		return this;
	}

	/// <summary>Sets the ignore_above value.</summary>
	public KeywordMultiFieldBuilder IgnoreAbove(int ignoreAbove)
	{
		_ignoreAbove = ignoreAbove;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator MultiFieldBuilder(KeywordMultiFieldBuilder builder)
	{
		builder._parent.SetDefinition(new KeywordFieldDefinition(
			builder._normalizer,
			builder._ignoreAbove
		));
		return builder._parent;
	}
}

/// <summary>Builder for text multi-fields.</summary>
public sealed class TextMultiFieldBuilder
{
	private readonly MultiFieldBuilder _parent;
	private string? _analyzer;
	private string? _searchAnalyzer;

	internal TextMultiFieldBuilder(MultiFieldBuilder parent) => _parent = parent;

	/// <summary>Sets the analyzer.</summary>
	public TextMultiFieldBuilder Analyzer(string analyzer)
	{
		_analyzer = analyzer;
		return this;
	}

	/// <summary>Sets the search analyzer.</summary>
	public TextMultiFieldBuilder SearchAnalyzer(string searchAnalyzer)
	{
		_searchAnalyzer = searchAnalyzer;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator MultiFieldBuilder(TextMultiFieldBuilder builder)
	{
		builder._parent.SetDefinition(new TextFieldDefinition(
			builder._analyzer,
			builder._searchAnalyzer
		));
		return builder._parent;
	}
}

/// <summary>Builder for search_as_you_type multi-fields.</summary>
public sealed class SearchAsYouTypeMultiFieldBuilder
{
	private readonly MultiFieldBuilder _parent;
	private string? _analyzer;
	private string? _searchAnalyzer;
	private int? _maxShingleSize;

	internal SearchAsYouTypeMultiFieldBuilder(MultiFieldBuilder parent) => _parent = parent;

	/// <summary>Sets the analyzer.</summary>
	public SearchAsYouTypeMultiFieldBuilder Analyzer(string analyzer)
	{
		_analyzer = analyzer;
		return this;
	}

	/// <summary>Sets the search analyzer.</summary>
	public SearchAsYouTypeMultiFieldBuilder SearchAnalyzer(string searchAnalyzer)
	{
		_searchAnalyzer = searchAnalyzer;
		return this;
	}

	/// <summary>Sets the max shingle size.</summary>
	public SearchAsYouTypeMultiFieldBuilder MaxShingleSize(int size)
	{
		_maxShingleSize = size;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator MultiFieldBuilder(SearchAsYouTypeMultiFieldBuilder builder)
	{
		builder._parent.SetDefinition(new SearchAsYouTypeFieldDefinition(
			builder._analyzer,
			builder._searchAnalyzer,
			builder._maxShingleSize
		));
		return builder._parent;
	}
}
