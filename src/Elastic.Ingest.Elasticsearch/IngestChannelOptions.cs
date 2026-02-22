// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using Elastic.Ingest.Elasticsearch.Strategies;
using Elastic.Mapping;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// Options for the composable <see cref="IngestChannel{TEvent}"/>.
/// Provide an <see cref="ElasticsearchTypeContext"/> for zero-config automatic strategy resolution,
/// or provide an explicit <see cref="IIngestStrategy{TEvent}"/> for full control.
/// </summary>
public class IngestChannelOptions<TEvent> : IngestChannelOptionsBase<TEvent>
	where TEvent : class
{
	/// <summary>
	/// Zero-config: auto-infers strategy from the <see cref="ElasticsearchTypeContext"/>.
	/// When <paramref name="batchTimestamp"/> is provided and <c>IndexPatternUseBatchDate</c> is true,
	/// the channel uses that timestamp instead of capturing <c>DateTimeOffset.UtcNow</c> at creation time.
	/// </summary>
	public IngestChannelOptions(ITransport transport, ElasticsearchTypeContext typeContext,
		DateTimeOffset? batchTimestamp = null)
		: this(transport, IngestStrategies.ForContext<TEvent>(typeContext, batchTimestamp), typeContext) { }

	/// <summary>
	/// Explicit strategy with optional mapping context.
	/// </summary>
	public IngestChannelOptions(ITransport transport, IIngestStrategy<TEvent> strategy,
		ElasticsearchTypeContext? typeContext = null) : base(transport)
	{
		Strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
		TypeContext = typeContext;
	}

	/// <summary>
	/// The composed strategy defining all channel behaviors.
	/// </summary>
	public IIngestStrategy<TEvent> Strategy { get; }

	/// <summary>
	/// Optional mapping context for AOT safety, GetMappingsJson, and GetSettingsJson hooks.
	/// Always available in the zero-config path; optional when an explicit strategy is provided.
	/// </summary>
	public ElasticsearchTypeContext? TypeContext { get; }
}
