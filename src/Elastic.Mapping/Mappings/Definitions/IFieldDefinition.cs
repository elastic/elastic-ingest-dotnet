// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Text.Json.Nodes;

namespace Elastic.Mapping.Mappings.Definitions;

/// <summary>Marker interface for field definitions.</summary>
public interface IFieldDefinition
{
	/// <summary>The Elasticsearch field type.</summary>
	string Type { get; }

	/// <summary>Converts the definition to a JSON object for Elasticsearch.</summary>
	JsonObject ToJson();
}

/// <summary>A text field definition.</summary>
public sealed record TextFieldDefinition(
	string? Analyzer = null,
	string? SearchAnalyzer = null,
	bool? Norms = null,
	bool? Index = null,
	string? CopyTo = null,
	IReadOnlyDictionary<string, IFieldDefinition>? MultiFields = null
) : IFieldDefinition
{
	public string Type => "text";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (Analyzer != null)
			obj["analyzer"] = Analyzer;

		if (SearchAnalyzer != null)
			obj["search_analyzer"] = SearchAnalyzer;

		if (Norms.HasValue)
			obj["norms"] = Norms.Value;

		if (Index.HasValue)
			obj["index"] = Index.Value;

		if (CopyTo != null)
			obj["copy_to"] = CopyTo;

		if (MultiFields is { Count: > 0 })
		{
			var fields = new JsonObject();
			foreach (var kvp in MultiFields)
				fields[kvp.Key] = kvp.Value.ToJson();
			obj["fields"] = fields;
		}

		return obj;
	}
}

/// <summary>A keyword field definition.</summary>
public sealed record KeywordFieldDefinition(
	string? Normalizer = null,
	int? IgnoreAbove = null,
	bool? DocValues = null,
	bool? Index = null,
	IReadOnlyDictionary<string, IFieldDefinition>? MultiFields = null
) : IFieldDefinition
{
	public string Type => "keyword";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (Normalizer != null)
			obj["normalizer"] = Normalizer;

		if (IgnoreAbove.HasValue)
			obj["ignore_above"] = IgnoreAbove.Value;

		if (DocValues.HasValue)
			obj["doc_values"] = DocValues.Value;

		if (Index.HasValue)
			obj["index"] = Index.Value;

		if (MultiFields is { Count: > 0 })
		{
			var fields = new JsonObject();
			foreach (var kvp in MultiFields)
				fields[kvp.Key] = kvp.Value.ToJson();
			obj["fields"] = fields;
		}

		return obj;
	}
}

/// <summary>A date field definition.</summary>
public sealed record DateFieldDefinition(
	string? Format = null,
	bool? DocValues = null,
	bool? Index = null
) : IFieldDefinition
{
	public string Type => "date";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (Format != null)
			obj["format"] = Format;

		if (DocValues.HasValue)
			obj["doc_values"] = DocValues.Value;

		if (Index.HasValue)
			obj["index"] = Index.Value;

		return obj;
	}
}

/// <summary>A long field definition.</summary>
public sealed record LongFieldDefinition(
	bool? DocValues = null,
	bool? Index = null
) : IFieldDefinition
{
	public string Type => "long";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (DocValues.HasValue)
			obj["doc_values"] = DocValues.Value;

		if (Index.HasValue)
			obj["index"] = Index.Value;

		return obj;
	}
}

/// <summary>An integer field definition.</summary>
public sealed record IntegerFieldDefinition(
	bool? DocValues = null,
	bool? Index = null
) : IFieldDefinition
{
	public string Type => "integer";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (DocValues.HasValue)
			obj["doc_values"] = DocValues.Value;

		if (Index.HasValue)
			obj["index"] = Index.Value;

		return obj;
	}
}

/// <summary>A short field definition.</summary>
public sealed record ShortFieldDefinition(
	bool? DocValues = null,
	bool? Index = null
) : IFieldDefinition
{
	public string Type => "short";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (DocValues.HasValue)
			obj["doc_values"] = DocValues.Value;

		if (Index.HasValue)
			obj["index"] = Index.Value;

		return obj;
	}
}

/// <summary>A byte field definition.</summary>
public sealed record ByteFieldDefinition(
	bool? DocValues = null,
	bool? Index = null
) : IFieldDefinition
{
	public string Type => "byte";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (DocValues.HasValue)
			obj["doc_values"] = DocValues.Value;

		if (Index.HasValue)
			obj["index"] = Index.Value;

		return obj;
	}
}

/// <summary>A double field definition.</summary>
public sealed record DoubleFieldDefinition(
	bool? DocValues = null,
	bool? Index = null
) : IFieldDefinition
{
	public string Type => "double";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (DocValues.HasValue)
			obj["doc_values"] = DocValues.Value;

		if (Index.HasValue)
			obj["index"] = Index.Value;

		return obj;
	}
}

/// <summary>A float field definition.</summary>
public sealed record FloatFieldDefinition(
	bool? DocValues = null,
	bool? Index = null
) : IFieldDefinition
{
	public string Type => "float";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (DocValues.HasValue)
			obj["doc_values"] = DocValues.Value;

		if (Index.HasValue)
			obj["index"] = Index.Value;

		return obj;
	}
}

/// <summary>A boolean field definition.</summary>
public sealed record BooleanFieldDefinition(
	bool? DocValues = null,
	bool? Index = null
) : IFieldDefinition
{
	public string Type => "boolean";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (DocValues.HasValue)
			obj["doc_values"] = DocValues.Value;

		if (Index.HasValue)
			obj["index"] = Index.Value;

		return obj;
	}
}

/// <summary>An IP field definition.</summary>
public sealed record IpFieldDefinition(
	bool? DocValues = null,
	bool? Index = null
) : IFieldDefinition
{
	public string Type => "ip";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (DocValues.HasValue)
			obj["doc_values"] = DocValues.Value;

		if (Index.HasValue)
			obj["index"] = Index.Value;

		return obj;
	}
}

/// <summary>A geo_point field definition.</summary>
public sealed record GeoPointFieldDefinition(
	bool? DocValues = null,
	bool? Index = null
) : IFieldDefinition
{
	public string Type => "geo_point";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (DocValues.HasValue)
			obj["doc_values"] = DocValues.Value;

		if (Index.HasValue)
			obj["index"] = Index.Value;

		return obj;
	}
}

/// <summary>A geo_shape field definition.</summary>
public sealed record GeoShapeFieldDefinition(
	bool? DocValues = null,
	bool? Index = null
) : IFieldDefinition
{
	public string Type => "geo_shape";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (DocValues.HasValue)
			obj["doc_values"] = DocValues.Value;

		if (Index.HasValue)
			obj["index"] = Index.Value;

		return obj;
	}
}

/// <summary>A nested field definition.</summary>
public sealed record NestedFieldDefinition(
	bool? IncludeInParent = null,
	bool? IncludeInRoot = null,
	IReadOnlyDictionary<string, IFieldDefinition>? Properties = null
) : IFieldDefinition
{
	public string Type => "nested";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (IncludeInParent.HasValue)
			obj["include_in_parent"] = IncludeInParent.Value;

		if (IncludeInRoot.HasValue)
			obj["include_in_root"] = IncludeInRoot.Value;

		if (Properties is { Count: > 0 })
		{
			var props = new JsonObject();
			foreach (var kvp in Properties)
				props[kvp.Key] = kvp.Value.ToJson();
			obj["properties"] = props;
		}

		return obj;
	}
}

/// <summary>An object field definition.</summary>
public sealed record ObjectFieldDefinition(
	bool? Enabled = null,
	IReadOnlyDictionary<string, IFieldDefinition>? Properties = null
) : IFieldDefinition
{
	public string Type => "object";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (Enabled.HasValue)
			obj["enabled"] = Enabled.Value;

		if (Properties is { Count: > 0 })
		{
			var props = new JsonObject();
			foreach (var kvp in Properties)
				props[kvp.Key] = kvp.Value.ToJson();
			obj["properties"] = props;
		}

		return obj;
	}
}

/// <summary>A completion field definition.</summary>
public sealed record CompletionFieldDefinition(
	string? Analyzer = null,
	string? SearchAnalyzer = null
) : IFieldDefinition
{
	public string Type => "completion";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (Analyzer != null)
			obj["analyzer"] = Analyzer;

		if (SearchAnalyzer != null)
			obj["search_analyzer"] = SearchAnalyzer;

		return obj;
	}
}

/// <summary>A dense_vector field definition.</summary>
public sealed record DenseVectorFieldDefinition(
	int Dims,
	string? Similarity = null,
	bool? Index = null
) : IFieldDefinition
{
	public string Type => "dense_vector";

	public JsonObject ToJson()
	{
		var obj = new JsonObject
		{
			["type"] = Type,
			["dims"] = Dims
		};

		if (Similarity != null)
			obj["similarity"] = Similarity;

		if (Index.HasValue)
			obj["index"] = Index.Value;

		return obj;
	}
}

/// <summary>A semantic_text field definition.</summary>
public sealed record SemanticTextFieldDefinition(
	string? InferenceId = null
) : IFieldDefinition
{
	public string Type => "semantic_text";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (InferenceId != null)
			obj["inference_id"] = InferenceId;

		return obj;
	}
}

/// <summary>A search_as_you_type field definition.</summary>
public sealed record SearchAsYouTypeFieldDefinition(
	string? Analyzer = null,
	string? SearchAnalyzer = null,
	int? MaxShingleSize = null
) : IFieldDefinition
{
	public string Type => "search_as_you_type";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (Analyzer != null)
			obj["analyzer"] = Analyzer;

		if (SearchAnalyzer != null)
			obj["search_analyzer"] = SearchAnalyzer;

		if (MaxShingleSize.HasValue)
			obj["max_shingle_size"] = MaxShingleSize.Value;

		return obj;
	}
}

/// <summary>A flattened field definition.</summary>
public sealed record FlattenedFieldDefinition(
	int? DepthLimit = null,
	int? IgnoreAbove = null,
	bool? DocValues = null,
	bool? Index = null
) : IFieldDefinition
{
	public string Type => "flattened";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (DepthLimit.HasValue)
			obj["depth_limit"] = DepthLimit.Value;

		if (IgnoreAbove.HasValue)
			obj["ignore_above"] = IgnoreAbove.Value;

		if (DocValues.HasValue)
			obj["doc_values"] = DocValues.Value;

		if (Index.HasValue)
			obj["index"] = Index.Value;

		return obj;
	}
}

/// <summary>A binary field definition.</summary>
public sealed record BinaryFieldDefinition(
	bool? DocValues = null,
	bool? Store = null
) : IFieldDefinition
{
	public string Type => "binary";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (DocValues.HasValue)
			obj["doc_values"] = DocValues.Value;

		if (Store.HasValue)
			obj["store"] = Store.Value;

		return obj;
	}
}

/// <summary>A range field definition (integer_range, long_range, float_range, double_range, date_range, ip_range).</summary>
public sealed record RangeFieldDefinition(
	string RangeType,
	bool? Coerce = null,
	bool? DocValues = null,
	bool? Index = null
) : IFieldDefinition
{
	public string Type => RangeType;

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (Coerce.HasValue)
			obj["coerce"] = Coerce.Value;

		if (DocValues.HasValue)
			obj["doc_values"] = DocValues.Value;

		if (Index.HasValue)
			obj["index"] = Index.Value;

		return obj;
	}
}

/// <summary>An alias field definition.</summary>
public sealed record AliasFieldDefinition(string Path) : IFieldDefinition
{
	public string Type => "alias";

	public JsonObject ToJson() => new()
	{
		["type"] = Type,
		["path"] = Path
	};
}

/// <summary>A join field definition.</summary>
public sealed record JoinFieldDefinition(
	IReadOnlyDictionary<string, IReadOnlyList<string>> Relations
) : IFieldDefinition
{
	public string Type => "join";

	public JsonObject ToJson()
	{
		var relations = new JsonObject();
		foreach (var kvp in Relations)
			relations[kvp.Key] = new JsonArray(kvp.Value.Select(v => JsonValue.Create(v)).ToArray());

		return new JsonObject
		{
			["type"] = Type,
			["relations"] = relations
		};
	}
}

/// <summary>A percolator field definition.</summary>
public sealed record PercolatorFieldDefinition : IFieldDefinition
{
	public string Type => "percolator";

	public JsonObject ToJson() => new() { ["type"] = Type };
}

/// <summary>A rank_feature field definition.</summary>
public sealed record RankFeatureFieldDefinition(
	bool? PositiveScoreImpact = null
) : IFieldDefinition
{
	public string Type => "rank_feature";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (PositiveScoreImpact.HasValue)
			obj["positive_score_impact"] = PositiveScoreImpact.Value;

		return obj;
	}
}

/// <summary>A rank_features field definition.</summary>
public sealed record RankFeaturesFieldDefinition : IFieldDefinition
{
	public string Type => "rank_features";

	public JsonObject ToJson() => new() { ["type"] = Type };
}

/// <summary>A histogram field definition.</summary>
public sealed record HistogramFieldDefinition : IFieldDefinition
{
	public string Type => "histogram";

	public JsonObject ToJson() => new() { ["type"] = Type };
}

/// <summary>A constant_keyword field definition.</summary>
public sealed record ConstantKeywordFieldDefinition(string? Value = null) : IFieldDefinition
{
	public string Type => "constant_keyword";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (Value != null)
			obj["value"] = Value;

		return obj;
	}
}

/// <summary>A wildcard field definition.</summary>
public sealed record WildcardFieldDefinition(
	int? IgnoreAbove = null
) : IFieldDefinition
{
	public string Type => "wildcard";

	public JsonObject ToJson()
	{
		var obj = new JsonObject { ["type"] = Type };

		if (IgnoreAbove.HasValue)
			obj["ignore_above"] = IgnoreAbove.Value;

		return obj;
	}
}

/// <summary>A version field definition.</summary>
public sealed record VersionFieldDefinition : IFieldDefinition
{
	public string Type => "version";

	public JsonObject ToJson() => new() { ["type"] = Type };
}
