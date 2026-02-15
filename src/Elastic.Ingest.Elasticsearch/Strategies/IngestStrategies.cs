// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
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
		ElasticsearchTypeContext tc) where TEvent : class
	{
		return tc.EntityTarget switch
		{
			EntityTarget.WiredStream => WiredStream<TEvent>(tc),
			EntityTarget.DataStream => DataStream<TEvent>(tc),
			_ => Index<TEvent>(tc)
		};
	}

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
				tc.IndexStrategy?.DataStreamName
				?? throw new InvalidOperationException("DataStreamName must be set for DataStream targets."),
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
	/// </summary>
	public static IIngestStrategy<TEvent> Index<TEvent>(
		ElasticsearchTypeContext tc,
		IBootstrapStrategy? bootstrap = null) where TEvent : class
	{
		return new IngestStrategy<TEvent>(tc,
			bootstrap ?? BootstrapStrategies.Index(),
			new TypeContextIndexIngestStrategy<TEvent>(
				tc,
				tc.IndexStrategy?.WriteTarget ?? typeof(TEvent).Name.ToLowerInvariant(),
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
	/// Resolves the template name from a <see cref="ElasticsearchTypeContext"/>.
	/// </summary>
	internal static string ResolveTemplateName(ElasticsearchTypeContext? tc)
	{
		if (tc == null)
			throw new InvalidOperationException(
				"TemplateName must be set explicitly when ElasticsearchTypeContext is not provided.");

		return tc.EntityTarget switch
		{
			EntityTarget.DataStream when tc.IndexStrategy?.DataStreamName != null =>
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
			EntityTarget.DataStream when tc.IndexStrategy?.DataStreamName != null =>
				$"{tc.IndexStrategy.Type}-{tc.IndexStrategy.Dataset}-*",
			EntityTarget.Index when tc.IndexStrategy?.WriteTarget != null =>
				$"{tc.IndexStrategy.WriteTarget}-*",
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
