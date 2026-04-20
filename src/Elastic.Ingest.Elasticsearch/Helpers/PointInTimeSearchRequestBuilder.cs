// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Text;

namespace Elastic.Ingest.Elasticsearch.Helpers;

/// <summary>
/// Builds the JSON body for PIT-based <c>POST /_search</c> requests.
/// <see cref="PointInTimeSearch{TDocument}"/> uses this; call directly only for custom transports or tests.
/// </summary>
public static class PointInTimeSearchRequestBuilder
{
	/// <summary>
	/// Serializes the search request body (PIT, pagination, optional query, slice, and optional <c>_source</c> filter).
	/// </summary>
	public static string BuildSearchBody(string pitId, PointInTimeSearchOptions options, string? searchAfter, int? sliceId, int? sliceMax)
	{
		var sb = new StringBuilder(256);
		sb.Append("{\"pit\":{\"id\":\"").Append(EscapeJson(pitId)).Append("\",\"keep_alive\":\"").Append(options.KeepAlive).Append("\"}");

		sb.Append(",\"size\":").Append(options.Size);

		var sort = options.Sort ?? "\"_shard_doc\"";
		sb.Append(",\"sort\":[").Append(sort).Append(']');

		if (options.QueryBody != null)
			sb.Append(",\"query\":").Append(options.QueryBody);

		if (searchAfter != null)
			sb.Append(",\"search_after\":").Append(searchAfter);

		if (sliceId.HasValue && sliceMax.HasValue)
			sb.Append(",\"slice\":{\"id\":").Append(sliceId.Value).Append(",\"max\":").Append(sliceMax.Value).Append('}');

		AppendSourceFilter(sb, options);

		sb.Append('}');
		return sb.ToString();
	}

	private static string EscapeJson(string value) =>
		value.Replace("\\", "\\\\").Replace("\"", "\\\"");

	private static void AppendSourceFilter(StringBuilder sb, PointInTimeSearchOptions options)
	{
		var hasIncludes = options.SourceIncludes is { Count: > 0 };
		var hasExcludes = options.SourceExcludes is { Count: > 0 };
		if (!hasIncludes && !hasExcludes)
			return;

		sb.Append(",\"_source\":{");
		if (hasIncludes)
		{
			sb.Append("\"includes\":[");
			AppendJsonStringArray(sb, options.SourceIncludes!);
			sb.Append(']');
		}
		if (hasExcludes)
		{
			if (hasIncludes)
				sb.Append(',');
			sb.Append("\"excludes\":[");
			AppendJsonStringArray(sb, options.SourceExcludes!);
			sb.Append(']');
		}
		sb.Append('}');
	}

	private static void AppendJsonStringArray(StringBuilder sb, IReadOnlyList<string> items)
	{
		for (var i = 0; i < items.Count; i++)
		{
			if (i > 0)
				sb.Append(',');
			sb.Append('"').Append(EscapeJson(items[i])).Append('"');
		}
	}
}
