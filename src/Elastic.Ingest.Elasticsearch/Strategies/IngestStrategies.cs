// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using Elastic.Ingest.Elasticsearch.Strategies.BootstrapSteps;
using Elastic.Mapping;
using static Elastic.Ingest.Elasticsearch.IngestChannelStatics;

namespace Elastic.Ingest.Elasticsearch.Strategies;

/// <summary>
/// Factory methods for creating common <see cref="IIngestStrategy{TEvent}"/> configurations.
/// </summary>
public static class IngestStrategies
{
	/// <summary>
	/// Auto-detect the appropriate strategy from the <see cref="ElasticsearchTypeContext.EntityTarget"/>.
	/// </summary>
	public static IIngestStrategy<TEvent> ForContext<TEvent>(
		ElasticsearchTypeContext tc) where TEvent : class =>
		tc.EntityTarget switch
		{
			EntityTarget.WiredStream => WiredStream<TEvent>(tc),
			EntityTarget.DataStream => DataStream<TEvent>(tc),
			_ => Index<TEvent>(tc)
		};

	/// <summary>
	/// Creates a data stream ingest strategy with optional custom bootstrap.
	/// </summary>
	public static IIngestStrategy<TEvent> DataStream<TEvent>(
		ElasticsearchTypeContext tc,
		IBootstrapStrategy? bootstrap = null) where TEvent : class
	{
		return new IngestStrategy<TEvent>(tc,
			bootstrap ?? BootstrapStrategies.DataStream(),
			new DataStreamIngestStrategy<TEvent>(
				ResolveDataStreamName(tc),
				DefaultBulkPathAndQuery),
			new AlwaysCreateProvisioning(),
			new NoAliasStrategy());
	}

	/// <summary>
	/// Creates a data stream ingest strategy with data stream lifecycle retention.
	/// </summary>
	public static IIngestStrategy<TEvent> DataStream<TEvent>(
		ElasticsearchTypeContext tc, string retention) where TEvent : class =>
		DataStream<TEvent>(tc, BootstrapStrategies.DataStream(retention));

	/// <summary>
	/// Creates an index ingest strategy with optional custom bootstrap.
	/// When <see cref="ElasticsearchTypeContext.IndexPatternUseBatchDate"/> is true and a
	/// <see cref="IndexStrategy.DatePattern"/> is configured, the index name is precomputed
	/// from <see cref="DateTimeOffset.UtcNow"/> at strategy creation time. All documents in the
	/// batch are written to this single fixed index (e.g., <c>my-index-2026.02.22.143055</c>).
	/// </summary>
	public static IIngestStrategy<TEvent> Index<TEvent>(
		ElasticsearchTypeContext tc,
		IBootstrapStrategy? bootstrap = null) where TEvent : class
	{
		var writeTarget = tc.IndexStrategy?.WriteTarget ?? typeof(TEvent).Name.ToLowerInvariant();
		var batchDate = tc.IndexPatternUseBatchDate ? DateTimeOffset.UtcNow : (DateTimeOffset?)null;
		var indexFormat = tc.IndexStrategy?.DatePattern != null && batchDate != null
			? $"{writeTarget}-{batchDate.Value.ToString(tc.IndexStrategy.DatePattern, System.Globalization.CultureInfo.InvariantCulture)}"
			: tc.IndexStrategy?.DatePattern != null
			? $"{writeTarget}-{{0:{tc.IndexStrategy.DatePattern}}}"
			: writeTarget;

		return new IngestStrategy<TEvent>(tc,
			bootstrap ?? BootstrapStrategies.Index(),
			new TypeContextIndexIngestStrategy<TEvent>(
				tc,
				indexFormat,
				DefaultBulkPathAndQuery),
			tc.GetContentHash != null
				? new HashBasedReuseProvisioning()
				: new AlwaysCreateProvisioning(),
			ResolveAliasStrategy(tc));
	}

	/// <summary>
	/// Creates a wired stream ingest strategy (bootstrap managed by Elasticsearch).
	/// </summary>
	public static IIngestStrategy<TEvent> WiredStream<TEvent>(
		ElasticsearchTypeContext tc) where TEvent : class
	{
		return new IngestStrategy<TEvent>(tc,
			BootstrapStrategies.None(),
			new WiredStreamIngestStrategy<TEvent>(DefaultBulkPathAndQuery),
			new AlwaysCreateProvisioning(),
			new NoAliasStrategy());
	}

	/// <summary>
	/// Resolves the effective data stream name from a <see cref="ElasticsearchTypeContext"/>.
	/// When <c>DataStreamName</c> is null (namespace omitted on the attribute), falls back to
	/// <c>{Type}-{Dataset}-{ResolveDefaultNamespace()}</c> using environment variables.
	/// </summary>
	internal static string ResolveDataStreamName(ElasticsearchTypeContext tc)
	{
		if (tc.IndexStrategy?.DataStreamName != null)
			return tc.IndexStrategy.DataStreamName;

		if (tc.IndexStrategy?.Type != null && tc.IndexStrategy?.Dataset != null)
		{
			var ns = tc.IndexStrategy.Namespace
				?? ElasticsearchTypeContext.ResolveDefaultNamespace();
			return $"{tc.IndexStrategy.Type}-{tc.IndexStrategy.Dataset}-{ns}";
		}

		throw new InvalidOperationException(
			"DataStream targets require either DataStreamName or Type+Dataset on IndexStrategy.");
	}

	/// <summary>
	/// Resolves the template name from a <see cref="ElasticsearchTypeContext"/>.
	/// </summary>
	internal static string ResolveTemplateName(ElasticsearchTypeContext? tc)
	{
		if (tc == null)
			throw new InvalidOperationException(
				"TemplateName must be set explicitly when ElasticsearchTypeContext is not provided.");

		return tc.EntityTarget switch
		{
			EntityTarget.DataStream when tc.IndexStrategy?.Type != null && tc.IndexStrategy?.Dataset != null =>
				$"{tc.IndexStrategy.Type}-{tc.IndexStrategy.Dataset}",
			EntityTarget.Index when tc.IndexStrategy?.WriteTarget != null =>
				$"{tc.IndexStrategy.WriteTarget}-template",
			_ => tc.MappedType?.Name.ToLowerInvariant() + "-template"
				?? throw new InvalidOperationException("Cannot resolve template name from TypeContext.")
		};
	}

	/// <summary>
	/// Resolves the template wildcard from a <see cref="ElasticsearchTypeContext"/>.
	/// </summary>
	internal static string ResolveTemplateWildcard(ElasticsearchTypeContext? tc)
	{
		if (tc == null)
			throw new InvalidOperationException(
				"TemplateWildcard must be set explicitly when ElasticsearchTypeContext is not provided.");

		return tc.EntityTarget switch
		{
			EntityTarget.DataStream when tc.IndexStrategy?.Type != null && tc.IndexStrategy?.Dataset != null =>
				$"{tc.IndexStrategy.Type}-{tc.IndexStrategy.Dataset}-*",
			// Date-rolling indices produce names like "prefix-2024.01.01" → wildcard matches all
			EntityTarget.Index when tc.IndexStrategy is { WriteTarget: { } wt, DatePattern: not null } =>
				$"{wt}-*",
			// Fixed-name index: use trailing wildcard so the pattern matches the exact index name
			// (idx-products*  matches  idx-products) while remaining a valid wildcard expression
			EntityTarget.Index when tc.IndexStrategy?.WriteTarget != null =>
				$"{tc.IndexStrategy.WriteTarget}*",
			_ => tc.MappedType?.Name.ToLowerInvariant() + "-*"
				?? throw new InvalidOperationException("Cannot resolve template wildcard from TypeContext.")
		};
	}

	internal static IAliasStrategy ResolveAliasStrategy(ElasticsearchTypeContext? tc)
	{
		if (tc?.SearchStrategy?.ReadAlias != null && tc.IndexStrategy?.WriteTarget != null)
		{
			var indexFormat = tc.IndexStrategy.DatePattern != null
				? $"{tc.IndexStrategy.WriteTarget}-{{0}}"
				: tc.IndexStrategy.WriteTarget;
			return new LatestAndSearchAliasStrategy(indexFormat);
		}

		return new NoAliasStrategy();
	}
}
