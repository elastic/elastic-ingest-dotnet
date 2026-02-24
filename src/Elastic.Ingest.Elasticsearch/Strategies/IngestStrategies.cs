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
		ElasticsearchTypeContext tc, DateTimeOffset? batchTimestamp = null) where TEvent : class =>
		tc.EntityTarget switch
		{
			EntityTarget.WiredStream => WiredStream<TEvent>(tc),
			EntityTarget.DataStream => DataStream<TEvent>(tc),
			_ => Index<TEvent>(tc, batchTimestamp: batchTimestamp)
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
				tc.ResolveDataStreamName(),
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
	/// from <paramref name="batchTimestamp"/> (or <see cref="DateTimeOffset.UtcNow"/> if null)
	/// at strategy creation time. All documents in the batch are written to this single fixed
	/// index (e.g., <c>my-index-2026.02.22.143055</c>).
	/// </summary>
	public static IIngestStrategy<TEvent> Index<TEvent>(
		ElasticsearchTypeContext tc,
		IBootstrapStrategy? bootstrap = null,
		DateTimeOffset? batchTimestamp = null) where TEvent : class
	{
		bootstrap ??= BootstrapStrategies.Index();

		var batchDate = tc.IndexPatternUseBatchDate ? batchTimestamp ?? DateTimeOffset.UtcNow : (DateTimeOffset?)null;
		var indexFormat = tc.ResolveIndexFormat(batchDate);

		var documentIngestStrategy = new TypeContextIndexIngestStrategy<TEvent>(tc, indexFormat, DefaultBulkPathAndQuery);
		IIndexProvisioningStrategy provisionStrategy = tc.GetContentHash != null ? new HashBasedReuseProvisioning() : new AlwaysCreateProvisioning();
		var resolveAliasStrategy = ResolveAliasStrategy(tc);

		return new IngestStrategy<TEvent>(tc, bootstrap, documentIngestStrategy, provisionStrategy, resolveAliasStrategy);
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


	internal static IAliasStrategy ResolveAliasStrategy(ElasticsearchTypeContext? tc)
	{
		if (tc?.SearchStrategy?.ReadAlias != null && tc.IndexStrategy?.WriteTarget != null)
			return new LatestAndSearchAliasStrategy(tc.ResolveAliasFormat());

		return new NoAliasStrategy();
	}
}
