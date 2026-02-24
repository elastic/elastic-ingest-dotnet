// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Collections.Generic;
using System.Globalization;
using Elastic.Mapping.Analysis;

namespace Elastic.Mapping;

/// <summary>
/// Type-specific context containing all Elasticsearch metadata generated at compile time.
/// Provides centralized resolve methods for index names, aliases, and search patterns.
/// </summary>
/// <param name="GetSettingsJson">Function that returns the index settings JSON.</param>
/// <param name="GetMappingsJson">Function that returns the mappings JSON.</param>
/// <param name="GetIndexJson">Function that returns the complete index JSON (settings + mappings).</param>
/// <param name="Hash">Combined hash of settings and mappings for change detection.</param>
/// <param name="SettingsHash">Hash of settings JSON only.</param>
/// <param name="MappingsHash">Hash of mappings JSON only.</param>
/// <param name="IndexStrategy">Write target configuration (alias, data stream name, date pattern).</param>
/// <param name="SearchStrategy">Search target configuration (pattern, read alias).</param>
/// <param name="EntityTarget">The Elasticsearch target type (Index, DataStream, WiredStream).</param>
/// <param name="DataStreamMode">Data stream mode for specialized types (Default, LogsDb, Tsdb).</param>
/// <param name="GetId">Generated accessor delegate for the [Id] property. Returns document _id.</param>
/// <param name="GetContentHash">Generated accessor delegate for the [ContentHash] property. Returns content hash for upserts.</param>
/// <param name="ContentHashFieldName">The JSON field name of the [ContentHash] property (resolved from [JsonPropertyName] or naming policy).</param>
/// <param name="GetTimestamp">Generated accessor delegate for the [Timestamp] property. Returns timestamp for data streams/date patterns.</param>
/// <param name="ConfigureAnalysis">Optional delegate for configuring analysis settings at runtime.</param>
/// <param name="MappedType">The CLR type this context maps.</param>
/// <param name="IndexPatternUseBatchDate">
/// When true, the index pattern uses a single fixed batch date (captured at channel creation)
/// instead of per-document timestamps. All documents in a batch are written to the same concrete index.
/// </param>
public record ElasticsearchTypeContext(
	Func<string> GetSettingsJson,
	Func<string> GetMappingsJson,
	Func<string> GetIndexJson,
	string Hash,
	string SettingsHash,
	string MappingsHash,
	IndexStrategy? IndexStrategy,
	SearchStrategy? SearchStrategy,
	EntityTarget EntityTarget,
	DataStreamMode DataStreamMode = DataStreamMode.Default,
	Func<object, string?>? GetId = null,
	Func<object, string?>? GetContentHash = null,
	string? ContentHashFieldName = null,
	Func<object, DateTimeOffset?>? GetTimestamp = null,
	Func<AnalysisBuilder, AnalysisBuilder>? ConfigureAnalysis = null,
	Type? MappedType = null,
	IReadOnlyDictionary<string, string>? IndexSettings = null,
	bool IndexPatternUseBatchDate = false
)
{
	// ── Resolve methods ──────────────────────────────────────────────────

	/// <summary>
	/// Resolves the concrete index name for a given timestamp.
	/// <para>
	/// When <see cref="Mapping.IndexStrategy.DatePattern"/> is configured, produces a
	/// timestamped name such as <c>"my-index-2026.02.24.120000"</c>.
	/// Otherwise returns the write target as-is.
	/// </para>
	/// </summary>
	public string ResolveIndexName(DateTimeOffset timestamp)
	{
		var wt = IndexStrategy?.WriteTarget
			?? throw new InvalidOperationException("No write target configured");
		return IndexStrategy?.DatePattern is { } pattern
			? $"{wt}-{timestamp.ToString(pattern, CultureInfo.InvariantCulture)}"
			: wt;
	}

	/// <summary>
	/// Builds a <see cref="string.Format"/>-compatible index name pattern for bulk operations.
	/// <para>
	/// When <paramref name="batchTimestamp"/> is provided and a date pattern is set,
	/// returns a precomputed concrete name. When only a date pattern is set, returns a
	/// format string like <c>"my-index-{0:yyyy.MM.dd}"</c>.
	/// Otherwise returns the fixed write target.
	/// </para>
	/// </summary>
	public string ResolveIndexFormat(DateTimeOffset? batchTimestamp = null)
	{
		var wt = IndexStrategy?.WriteTarget
			?? throw new InvalidOperationException("No write target configured");
		if (IndexStrategy?.DatePattern is { } pattern)
		{
			return batchTimestamp is { } ts
				? $"{wt}-{ts.ToString(pattern, CultureInfo.InvariantCulture)}"
				: $"{wt}-{{0:{pattern}}}";
		}
		return wt;
	}

	/// <summary>
	/// Resolves the write alias name.
	/// <para>
	/// When <see cref="Mapping.IndexStrategy.DatePattern"/> is set, returns
	/// <c>"{writeTarget}-latest"</c>. Otherwise returns the write target directly.
	/// </para>
	/// </summary>
	public string ResolveWriteAlias()
	{
		var wt = IndexStrategy?.WriteTarget
			?? throw new InvalidOperationException("No write target configured");
		return IndexStrategy?.DatePattern != null ? $"{wt}-latest" : wt;
	}

	/// <summary>
	/// Resolves the best read target: <see cref="Mapping.SearchStrategy.ReadAlias"/>
	/// if available, otherwise falls back to <see cref="ResolveWriteAlias"/>.
	/// </summary>
	public string ResolveReadTarget()
	{
		var readAlias = SearchStrategy?.ReadAlias;
		return !string.IsNullOrEmpty(readAlias) ? readAlias! : ResolveWriteAlias();
	}

	/// <summary>
	/// Resolves the wildcard search pattern for index templates and search operations.
	/// <para>
	/// Date-rolling indices: <c>"{writeTarget}-*"</c>.
	/// Fixed-name indices: <c>"{writeTarget}*"</c>.
	/// Data streams: <c>"{type}-{dataset}-*"</c>.
	/// </para>
	/// </summary>
	public string ResolveSearchPattern()
	{
		return EntityTarget switch
		{
			EntityTarget.DataStream or EntityTarget.WiredStream
				when IndexStrategy?.Type != null && IndexStrategy?.Dataset != null =>
				$"{IndexStrategy.Type}-{IndexStrategy.Dataset}-*",
			EntityTarget.Index when IndexStrategy is { WriteTarget: { } wt, DatePattern: not null } =>
				$"{wt}-*",
			EntityTarget.Index when IndexStrategy?.WriteTarget != null =>
				$"{IndexStrategy.WriteTarget}*",
			_ => MappedType?.Name.ToLowerInvariant() + "-*"
				?? throw new InvalidOperationException("Cannot resolve search pattern from context.")
		};
	}

	/// <summary>
	/// Resolves the format string for alias operations.
	/// <para>
	/// When <see cref="Mapping.IndexStrategy.DatePattern"/> is set, returns
	/// <c>"{writeTarget}-{0}"</c> for use with <see cref="string.Format(string,object)"/>.
	/// Otherwise returns the fixed write target.
	/// </para>
	/// </summary>
	public string ResolveAliasFormat()
	{
		var wt = IndexStrategy?.WriteTarget
			?? throw new InvalidOperationException("No write target configured");
		return IndexStrategy?.DatePattern != null ? $"{wt}-{{0}}" : wt;
	}

	/// <summary>
	/// Resolves the effective data stream name.
	/// <para>
	/// When <see cref="Mapping.IndexStrategy.DataStreamName"/> is null (namespace omitted),
	/// falls back to <c>{Type}-{Dataset}-{ResolveDefaultNamespace()}</c>.
	/// </para>
	/// </summary>
	public string ResolveDataStreamName()
	{
		if (IndexStrategy?.DataStreamName != null)
			return IndexStrategy.DataStreamName;

		if (IndexStrategy?.Type != null && IndexStrategy?.Dataset != null)
		{
			var ns = IndexStrategy.Namespace ?? ResolveDefaultNamespace();
			return $"{IndexStrategy.Type}-{IndexStrategy.Dataset}-{ns}";
		}

		throw new InvalidOperationException(
			"DataStream targets require either DataStreamName or Type+Dataset on IndexStrategy.");
	}

	/// <summary>
	/// Resolves the component/index template name.
	/// </summary>
	public string ResolveTemplateName() =>
		EntityTarget switch
		{
			EntityTarget.DataStream or EntityTarget.WiredStream
				when IndexStrategy?.Type != null && IndexStrategy?.Dataset != null =>
				$"{IndexStrategy.Type}-{IndexStrategy.Dataset}",
			EntityTarget.Index when IndexStrategy?.WriteTarget != null =>
				$"{IndexStrategy.WriteTarget}-template",
			_ => MappedType?.Name.ToLowerInvariant() + "-template"
				?? throw new InvalidOperationException("Cannot resolve template name from context.")
		};

	// ── With* methods ────────────────────────────────────────────────────

	/// <summary>
	/// Returns a copy of this context with the data stream namespace replaced.
	/// </summary>
	public ElasticsearchTypeContext WithNamespace(string ns) =>
		this with
		{
			IndexStrategy = IndexStrategy == null ? null : new IndexStrategy
			{
				DataStreamName = IndexStrategy.Type != null && IndexStrategy.Dataset != null
					? $"{IndexStrategy.Type}-{IndexStrategy.Dataset}-{ns}"
					: IndexStrategy.DataStreamName,
				Type = IndexStrategy.Type,
				Dataset = IndexStrategy.Dataset,
				Namespace = ns,
				WriteTarget = IndexStrategy.WriteTarget,
				DatePattern = IndexStrategy.DatePattern,
			},
			SearchStrategy = SearchStrategy == null ? null : new SearchStrategy
			{
				Pattern = SearchStrategy.Pattern,
				ReadAlias = SearchStrategy.ReadAlias,
			}
		};

	/// <summary>
	/// Returns a copy of this context with the index write target replaced.
	/// Search pattern is auto-derived based on the presence of <see cref="Mapping.IndexStrategy.DatePattern"/>.
	/// </summary>
	public ElasticsearchTypeContext WithIndexName(string writeTarget) =>
		this with
		{
			IndexStrategy = IndexStrategy == null ? null : new IndexStrategy
			{
				WriteTarget = writeTarget,
				DatePattern = IndexStrategy.DatePattern,
				DataStreamName = IndexStrategy.DataStreamName,
				Type = IndexStrategy.Type,
				Dataset = IndexStrategy.Dataset,
				Namespace = IndexStrategy.Namespace,
			},
			SearchStrategy = new SearchStrategy
			{
				ReadAlias = SearchStrategy?.ReadAlias,
				Pattern = IndexStrategy?.DatePattern != null ? $"{writeTarget}-*" : null,
			}
		};

	/// <summary>
	/// Returns a copy of this context with the namespace resolved from environment variables.
	/// </summary>
	public ElasticsearchTypeContext WithEnvironmentNamespace() =>
		WithNamespace(ResolveDefaultNamespace());

	// ── Static helpers ───────────────────────────────────────────────────

	/// <summary>
	/// Resolves the default namespace from environment variables in priority order:
	/// <c>DOTNET_ENVIRONMENT</c> &gt; <c>ASPNETCORE_ENVIRONMENT</c> &gt; <c>ENVIRONMENT</c>,
	/// falling back to <c>"development"</c>.
	/// </summary>
	public static string ResolveDefaultNamespace() =>
		Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
		?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
		?? Environment.GetEnvironmentVariable("ENVIRONMENT")
		?? "development";
}
