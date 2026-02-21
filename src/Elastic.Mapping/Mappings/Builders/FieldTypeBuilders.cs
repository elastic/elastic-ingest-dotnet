// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Mappings.Definitions;

namespace Elastic.Mapping.Mappings.Builders;

/// <summary>Builder for text fields.</summary>
public sealed class TextFieldBuilder
{
	private readonly FieldBuilder _parent;
	private string? _analyzer;
	private string? _searchAnalyzer;
	private bool? _norms;
	private bool? _index;
	private string? _copyTo;
	private readonly List<(string Name, IFieldDefinition Definition)> _multiFields = [];

	internal TextFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets the analyzer.</summary>
	public TextFieldBuilder Analyzer(string analyzer)
	{
		_analyzer = analyzer;
		return this;
	}

	/// <summary>Sets the search analyzer.</summary>
	public TextFieldBuilder SearchAnalyzer(string searchAnalyzer)
	{
		_searchAnalyzer = searchAnalyzer;
		return this;
	}

	/// <summary>Sets whether norms are enabled.</summary>
	public TextFieldBuilder Norms(bool norms)
	{
		_norms = norms;
		return this;
	}

	/// <summary>Sets whether the field is indexed.</summary>
	public TextFieldBuilder Index(bool index)
	{
		_index = index;
		return this;
	}

	/// <summary>Sets the copy_to target field.</summary>
	public TextFieldBuilder CopyTo(string copyTo)
	{
		_copyTo = copyTo;
		return this;
	}

	/// <summary>Adds a multi-field.</summary>
	public TextFieldBuilder MultiField(string name, Func<MultiFieldBuilder, MultiFieldBuilder> configure)
	{
		var builder = new MultiFieldBuilder();
		_ = configure(builder);
		_multiFields.Add((name, builder.GetDefinition()));
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(TextFieldBuilder builder)
	{
		builder._parent.SetDefinition(new TextFieldDefinition(
			builder._analyzer,
			builder._searchAnalyzer,
			builder._norms,
			builder._index,
			builder._copyTo,
			builder._multiFields.Count > 0
				? builder._multiFields.ToDictionary(x => x.Name, x => x.Definition)
				: null
		));
		return builder._parent;
	}
}

/// <summary>Builder for keyword fields.</summary>
public sealed class KeywordFieldBuilder
{
	private readonly FieldBuilder _parent;
	private string? _normalizer;
	private int? _ignoreAbove;
	private bool? _docValues;
	private bool? _index;
	private readonly List<(string Name, IFieldDefinition Definition)> _multiFields = [];

	internal KeywordFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets the normalizer.</summary>
	public KeywordFieldBuilder Normalizer(string normalizer)
	{
		_normalizer = normalizer;
		return this;
	}

	/// <summary>Sets the ignore_above value.</summary>
	public KeywordFieldBuilder IgnoreAbove(int ignoreAbove)
	{
		_ignoreAbove = ignoreAbove;
		return this;
	}

	/// <summary>Sets whether doc_values are enabled.</summary>
	public KeywordFieldBuilder DocValues(bool docValues)
	{
		_docValues = docValues;
		return this;
	}

	/// <summary>Sets whether the field is indexed.</summary>
	public KeywordFieldBuilder Index(bool index)
	{
		_index = index;
		return this;
	}

	/// <summary>Adds a multi-field.</summary>
	public KeywordFieldBuilder MultiField(string name, Func<MultiFieldBuilder, MultiFieldBuilder> configure)
	{
		var builder = new MultiFieldBuilder();
		_ = configure(builder);
		_multiFields.Add((name, builder.GetDefinition()));
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(KeywordFieldBuilder builder)
	{
		builder._parent.SetDefinition(new KeywordFieldDefinition(
			builder._normalizer,
			builder._ignoreAbove,
			builder._docValues,
			builder._index,
			builder._multiFields.Count > 0
				? builder._multiFields.ToDictionary(x => x.Name, x => x.Definition)
				: null
		));
		return builder._parent;
	}
}

/// <summary>Builder for date fields.</summary>
public sealed class DateFieldBuilder
{
	private readonly FieldBuilder _parent;
	private string? _format;
	private bool? _docValues;
	private bool? _index;

	internal DateFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets the date format.</summary>
	public DateFieldBuilder Format(string format)
	{
		_format = format;
		return this;
	}

	/// <summary>Sets whether doc_values are enabled.</summary>
	public DateFieldBuilder DocValues(bool docValues)
	{
		_docValues = docValues;
		return this;
	}

	/// <summary>Sets whether the field is indexed.</summary>
	public DateFieldBuilder Index(bool index)
	{
		_index = index;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(DateFieldBuilder builder)
	{
		builder._parent.SetDefinition(new DateFieldDefinition(
			builder._format,
			builder._docValues,
			builder._index
		));
		return builder._parent;
	}
}

/// <summary>Builder for long fields.</summary>
public sealed class LongFieldBuilder
{
	private readonly FieldBuilder _parent;
	private bool? _docValues;
	private bool? _index;

	internal LongFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets whether doc_values are enabled.</summary>
	public LongFieldBuilder DocValues(bool docValues)
	{
		_docValues = docValues;
		return this;
	}

	/// <summary>Sets whether the field is indexed.</summary>
	public LongFieldBuilder Index(bool index)
	{
		_index = index;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(LongFieldBuilder builder)
	{
		builder._parent.SetDefinition(new LongFieldDefinition(
			builder._docValues,
			builder._index
		));
		return builder._parent;
	}
}

/// <summary>Builder for integer fields.</summary>
public sealed class IntegerFieldBuilder
{
	private readonly FieldBuilder _parent;
	private bool? _docValues;
	private bool? _index;

	internal IntegerFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets whether doc_values are enabled.</summary>
	public IntegerFieldBuilder DocValues(bool docValues)
	{
		_docValues = docValues;
		return this;
	}

	/// <summary>Sets whether the field is indexed.</summary>
	public IntegerFieldBuilder Index(bool index)
	{
		_index = index;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(IntegerFieldBuilder builder)
	{
		builder._parent.SetDefinition(new IntegerFieldDefinition(
			builder._docValues,
			builder._index
		));
		return builder._parent;
	}
}

/// <summary>Builder for short fields.</summary>
public sealed class ShortFieldBuilder
{
	private readonly FieldBuilder _parent;
	private bool? _docValues;
	private bool? _index;

	internal ShortFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets whether doc_values are enabled.</summary>
	public ShortFieldBuilder DocValues(bool docValues)
	{
		_docValues = docValues;
		return this;
	}

	/// <summary>Sets whether the field is indexed.</summary>
	public ShortFieldBuilder Index(bool index)
	{
		_index = index;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(ShortFieldBuilder builder)
	{
		builder._parent.SetDefinition(new ShortFieldDefinition(
			builder._docValues,
			builder._index
		));
		return builder._parent;
	}
}

/// <summary>Builder for byte fields.</summary>
public sealed class ByteFieldBuilder
{
	private readonly FieldBuilder _parent;
	private bool? _docValues;
	private bool? _index;

	internal ByteFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets whether doc_values are enabled.</summary>
	public ByteFieldBuilder DocValues(bool docValues)
	{
		_docValues = docValues;
		return this;
	}

	/// <summary>Sets whether the field is indexed.</summary>
	public ByteFieldBuilder Index(bool index)
	{
		_index = index;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(ByteFieldBuilder builder)
	{
		builder._parent.SetDefinition(new ByteFieldDefinition(
			builder._docValues,
			builder._index
		));
		return builder._parent;
	}
}

/// <summary>Builder for double fields.</summary>
public sealed class DoubleFieldBuilder
{
	private readonly FieldBuilder _parent;
	private bool? _docValues;
	private bool? _index;

	internal DoubleFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets whether doc_values are enabled.</summary>
	public DoubleFieldBuilder DocValues(bool docValues)
	{
		_docValues = docValues;
		return this;
	}

	/// <summary>Sets whether the field is indexed.</summary>
	public DoubleFieldBuilder Index(bool index)
	{
		_index = index;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(DoubleFieldBuilder builder)
	{
		builder._parent.SetDefinition(new DoubleFieldDefinition(
			builder._docValues,
			builder._index
		));
		return builder._parent;
	}
}

/// <summary>Builder for float fields.</summary>
public sealed class FloatFieldBuilder
{
	private readonly FieldBuilder _parent;
	private bool? _docValues;
	private bool? _index;

	internal FloatFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets whether doc_values are enabled.</summary>
	public FloatFieldBuilder DocValues(bool docValues)
	{
		_docValues = docValues;
		return this;
	}

	/// <summary>Sets whether the field is indexed.</summary>
	public FloatFieldBuilder Index(bool index)
	{
		_index = index;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(FloatFieldBuilder builder)
	{
		builder._parent.SetDefinition(new FloatFieldDefinition(
			builder._docValues,
			builder._index
		));
		return builder._parent;
	}
}

/// <summary>Builder for boolean fields.</summary>
public sealed class BooleanFieldBuilder
{
	private readonly FieldBuilder _parent;
	private bool? _docValues;
	private bool? _index;

	internal BooleanFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets whether doc_values are enabled.</summary>
	public BooleanFieldBuilder DocValues(bool docValues)
	{
		_docValues = docValues;
		return this;
	}

	/// <summary>Sets whether the field is indexed.</summary>
	public BooleanFieldBuilder Index(bool index)
	{
		_index = index;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(BooleanFieldBuilder builder)
	{
		builder._parent.SetDefinition(new BooleanFieldDefinition(
			builder._docValues,
			builder._index
		));
		return builder._parent;
	}
}

/// <summary>Builder for IP fields.</summary>
public sealed class IpFieldBuilder
{
	private readonly FieldBuilder _parent;
	private bool? _docValues;
	private bool? _index;

	internal IpFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets whether doc_values are enabled.</summary>
	public IpFieldBuilder DocValues(bool docValues)
	{
		_docValues = docValues;
		return this;
	}

	/// <summary>Sets whether the field is indexed.</summary>
	public IpFieldBuilder Index(bool index)
	{
		_index = index;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(IpFieldBuilder builder)
	{
		builder._parent.SetDefinition(new IpFieldDefinition(
			builder._docValues,
			builder._index
		));
		return builder._parent;
	}
}

/// <summary>Builder for geo_point fields.</summary>
public sealed class GeoPointFieldBuilder
{
	private readonly FieldBuilder _parent;
	private bool? _docValues;
	private bool? _index;

	internal GeoPointFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets whether doc_values are enabled.</summary>
	public GeoPointFieldBuilder DocValues(bool docValues)
	{
		_docValues = docValues;
		return this;
	}

	/// <summary>Sets whether the field is indexed.</summary>
	public GeoPointFieldBuilder Index(bool index)
	{
		_index = index;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(GeoPointFieldBuilder builder)
	{
		builder._parent.SetDefinition(new GeoPointFieldDefinition(
			builder._docValues,
			builder._index
		));
		return builder._parent;
	}
}

/// <summary>Builder for geo_shape fields.</summary>
public sealed class GeoShapeFieldBuilder
{
	private readonly FieldBuilder _parent;
	private bool? _docValues;
	private bool? _index;

	internal GeoShapeFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets whether doc_values are enabled.</summary>
	public GeoShapeFieldBuilder DocValues(bool docValues)
	{
		_docValues = docValues;
		return this;
	}

	/// <summary>Sets whether the field is indexed.</summary>
	public GeoShapeFieldBuilder Index(bool index)
	{
		_index = index;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(GeoShapeFieldBuilder builder)
	{
		builder._parent.SetDefinition(new GeoShapeFieldDefinition(
			builder._docValues,
			builder._index
		));
		return builder._parent;
	}
}

/// <summary>Builder for nested fields.</summary>
public sealed class NestedFieldBuilder
{
	private readonly FieldBuilder _parent;
	private bool? _includeInParent;
	private bool? _includeInRoot;

	internal NestedFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets whether to include in parent.</summary>
	public NestedFieldBuilder IncludeInParent(bool include = true)
	{
		_includeInParent = include;
		return this;
	}

	/// <summary>Sets whether to include in root.</summary>
	public NestedFieldBuilder IncludeInRoot(bool include = true)
	{
		_includeInRoot = include;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(NestedFieldBuilder builder)
	{
		builder._parent.SetDefinition(new NestedFieldDefinition(
			builder._includeInParent,
			builder._includeInRoot
		));
		return builder._parent;
	}
}

/// <summary>Builder for object fields.</summary>
public sealed class ObjectFieldBuilder
{
	private readonly FieldBuilder _parent;
	private bool? _enabled;

	internal ObjectFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets whether the object is enabled.</summary>
	public ObjectFieldBuilder Enabled(bool enabled)
	{
		_enabled = enabled;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(ObjectFieldBuilder builder)
	{
		builder._parent.SetDefinition(new ObjectFieldDefinition(builder._enabled));
		return builder._parent;
	}
}

/// <summary>Builder for completion fields.</summary>
public sealed class CompletionFieldBuilder
{
	private readonly FieldBuilder _parent;
	private string? _analyzer;
	private string? _searchAnalyzer;

	internal CompletionFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets the analyzer.</summary>
	public CompletionFieldBuilder Analyzer(string analyzer)
	{
		_analyzer = analyzer;
		return this;
	}

	/// <summary>Sets the search analyzer.</summary>
	public CompletionFieldBuilder SearchAnalyzer(string searchAnalyzer)
	{
		_searchAnalyzer = searchAnalyzer;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(CompletionFieldBuilder builder)
	{
		builder._parent.SetDefinition(new CompletionFieldDefinition(
			builder._analyzer,
			builder._searchAnalyzer
		));
		return builder._parent;
	}
}

/// <summary>Builder for dense_vector fields.</summary>
public sealed class DenseVectorFieldBuilder
{
	private readonly FieldBuilder _parent;
	private int _dims;
	private string? _similarity;
	private bool? _index;

	internal DenseVectorFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets the dimensions.</summary>
	public DenseVectorFieldBuilder Dims(int dims)
	{
		_dims = dims;
		return this;
	}

	/// <summary>Sets the similarity measure.</summary>
	public DenseVectorFieldBuilder Similarity(string similarity)
	{
		_similarity = similarity;
		return this;
	}

	/// <summary>Sets whether the field is indexed.</summary>
	public DenseVectorFieldBuilder Index(bool index)
	{
		_index = index;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(DenseVectorFieldBuilder builder)
	{
		builder._parent.SetDefinition(new DenseVectorFieldDefinition(
			builder._dims,
			builder._similarity,
			builder._index
		));
		return builder._parent;
	}
}

/// <summary>Builder for semantic_text fields.</summary>
public sealed class SemanticTextFieldBuilder
{
	private readonly FieldBuilder _parent;
	private string? _inferenceId;

	internal SemanticTextFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets the inference ID.</summary>
	public SemanticTextFieldBuilder InferenceId(string inferenceId)
	{
		_inferenceId = inferenceId;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(SemanticTextFieldBuilder builder)
	{
		builder._parent.SetDefinition(new SemanticTextFieldDefinition(builder._inferenceId));
		return builder._parent;
	}
}

/// <summary>Builder for search_as_you_type fields.</summary>
public sealed class SearchAsYouTypeFieldBuilder
{
	private readonly FieldBuilder _parent;
	private string? _analyzer;
	private string? _searchAnalyzer;
	private int? _maxShingleSize;

	internal SearchAsYouTypeFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets the analyzer.</summary>
	public SearchAsYouTypeFieldBuilder Analyzer(string analyzer)
	{
		_analyzer = analyzer;
		return this;
	}

	/// <summary>Sets the search analyzer.</summary>
	public SearchAsYouTypeFieldBuilder SearchAnalyzer(string searchAnalyzer)
	{
		_searchAnalyzer = searchAnalyzer;
		return this;
	}

	/// <summary>Sets the max shingle size.</summary>
	public SearchAsYouTypeFieldBuilder MaxShingleSize(int size)
	{
		_maxShingleSize = size;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(SearchAsYouTypeFieldBuilder builder)
	{
		builder._parent.SetDefinition(new SearchAsYouTypeFieldDefinition(
			builder._analyzer,
			builder._searchAnalyzer,
			builder._maxShingleSize
		));
		return builder._parent;
	}
}

/// <summary>Builder for flattened fields.</summary>
public sealed class FlattenedFieldBuilder
{
	private readonly FieldBuilder _parent;
	private int? _depthLimit;
	private int? _ignoreAbove;
	private bool? _docValues;
	private bool? _index;

	internal FlattenedFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets the depth limit.</summary>
	public FlattenedFieldBuilder DepthLimit(int limit)
	{
		_depthLimit = limit;
		return this;
	}

	/// <summary>Sets the ignore_above value.</summary>
	public FlattenedFieldBuilder IgnoreAbove(int ignoreAbove)
	{
		_ignoreAbove = ignoreAbove;
		return this;
	}

	/// <summary>Sets whether doc_values are enabled.</summary>
	public FlattenedFieldBuilder DocValues(bool docValues)
	{
		_docValues = docValues;
		return this;
	}

	/// <summary>Sets whether the field is indexed.</summary>
	public FlattenedFieldBuilder Index(bool index)
	{
		_index = index;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(FlattenedFieldBuilder builder)
	{
		builder._parent.SetDefinition(new FlattenedFieldDefinition(
			builder._depthLimit,
			builder._ignoreAbove,
			builder._docValues,
			builder._index
		));
		return builder._parent;
	}
}

/// <summary>Builder for binary fields.</summary>
public sealed class BinaryFieldBuilder
{
	private readonly FieldBuilder _parent;
	private bool? _docValues;
	private bool? _store;

	internal BinaryFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets whether doc_values are enabled.</summary>
	public BinaryFieldBuilder DocValues(bool docValues)
	{
		_docValues = docValues;
		return this;
	}

	/// <summary>Sets whether to store the field.</summary>
	public BinaryFieldBuilder Store(bool store)
	{
		_store = store;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(BinaryFieldBuilder builder)
	{
		builder._parent.SetDefinition(new BinaryFieldDefinition(
			builder._docValues,
			builder._store
		));
		return builder._parent;
	}
}

/// <summary>Builder for range fields.</summary>
public sealed class RangeFieldBuilder
{
	private readonly FieldBuilder _parent;
	private readonly string _rangeType;
	private bool? _coerce;
	private bool? _docValues;
	private bool? _index;

	internal RangeFieldBuilder(FieldBuilder parent, string rangeType)
	{
		_parent = parent;
		_rangeType = rangeType;
	}

	/// <summary>Sets whether to coerce values.</summary>
	public RangeFieldBuilder Coerce(bool coerce)
	{
		_coerce = coerce;
		return this;
	}

	/// <summary>Sets whether doc_values are enabled.</summary>
	public RangeFieldBuilder DocValues(bool docValues)
	{
		_docValues = docValues;
		return this;
	}

	/// <summary>Sets whether the field is indexed.</summary>
	public RangeFieldBuilder Index(bool index)
	{
		_index = index;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(RangeFieldBuilder builder)
	{
		builder._parent.SetDefinition(new RangeFieldDefinition(
			builder._rangeType,
			builder._coerce,
			builder._docValues,
			builder._index
		));
		return builder._parent;
	}
}

/// <summary>Builder for rank_feature fields.</summary>
public sealed class RankFeatureFieldBuilder
{
	private readonly FieldBuilder _parent;
	private bool? _positiveScoreImpact;

	internal RankFeatureFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets whether the feature has a positive score impact.</summary>
	public RankFeatureFieldBuilder PositiveScoreImpact(bool positive)
	{
		_positiveScoreImpact = positive;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(RankFeatureFieldBuilder builder)
	{
		builder._parent.SetDefinition(new RankFeatureFieldDefinition(builder._positiveScoreImpact));
		return builder._parent;
	}
}

/// <summary>Builder for constant_keyword fields.</summary>
public sealed class ConstantKeywordFieldBuilder
{
	private readonly FieldBuilder _parent;
	private string? _value;

	internal ConstantKeywordFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets the constant value.</summary>
	public ConstantKeywordFieldBuilder Value(string value)
	{
		_value = value;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(ConstantKeywordFieldBuilder builder)
	{
		builder._parent.SetDefinition(new ConstantKeywordFieldDefinition(builder._value));
		return builder._parent;
	}
}

/// <summary>Builder for wildcard fields.</summary>
public sealed class WildcardFieldBuilder
{
	private readonly FieldBuilder _parent;
	private int? _ignoreAbove;

	internal WildcardFieldBuilder(FieldBuilder parent) => _parent = parent;

	/// <summary>Sets the ignore_above value.</summary>
	public WildcardFieldBuilder IgnoreAbove(int ignoreAbove)
	{
		_ignoreAbove = ignoreAbove;
		return this;
	}

	/// <summary>Implicit conversion finalizes the builder and returns the parent.</summary>
	public static implicit operator FieldBuilder(WildcardFieldBuilder builder)
	{
		builder._parent.SetDefinition(new WildcardFieldDefinition(builder._ignoreAbove));
		return builder._parent;
	}
}
