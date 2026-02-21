// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using Elastic.Mapping.Analysis;

namespace Elastic.Mapping;

/// <summary>
/// Type-specific context containing all Elasticsearch metadata generated at compile time.
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
	Type? MappedType = null
)
{
	/// <summary>
	/// Returns a copy of this context with the data stream namespace replaced.
	/// Updates <see cref="IndexStrategy.DataStreamName"/> to reflect the new namespace.
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
	/// Returns a copy of this context with the index write target, read alias, and search pattern replaced.
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
				ReadAlias = writeTarget,
				Pattern = $"{writeTarget}-*"
			}
		};

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

	/// <summary>
	/// Returns a copy of this context with the namespace resolved from environment variables.
	/// Uses <see cref="ResolveDefaultNamespace"/> to determine the namespace.
	/// </summary>
	public ElasticsearchTypeContext WithEnvironmentNamespace() =>
		WithNamespace(ResolveDefaultNamespace());
}
