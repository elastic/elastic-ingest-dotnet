// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elastic.Ingest.Elasticsearch.Bulk;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Ingest.Transport;
using Elastic.Transport;
using Elastic.Transport.Products.Elasticsearch;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// Base options implementation for <see cref="IngestChannelBase{TEvent,TChannelOptions}"/> implementations
/// </summary>
public abstract class IngestChannelOptionsBase<TEvent> : TransportChannelOptionsBase<TEvent, BulkResponse, BulkResponseItem>
{
	/// <inheritdoc cref="IngestChannelBase{TEvent,TChannelOptions}"/>
	protected IngestChannelOptionsBase(ITransport transport) : base(transport) { }

	/// <summary>
	/// Export option, Optionally provide a custom write implementation for <typeparamref name="TEvent"/>
	/// </summary>
	public IElasticsearchEventWriter<TEvent>? EventWriter { get; set; }

#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
	/// <summary>
	/// Expert option,
	/// This will eagerly serialize to <see cref="ReadOnlyMemory{TEvent}"/> and use <see cref="PostData.ReadOnlyMemory"/>.
	/// If false (default) the channel will use <see cref="PostData.StreamHandler{T}"/> to directly write to the stream.
	/// </summary>
	#else
	/// <summary>
	/// Expert option, only available in netstandard2.1+ compatible runtimes to evaluate serialization approaches
	/// </summary>
	#endif
	[Obsolete("Temporary exposed expert option, used to evaluate two different approaches to serialization")]
	public bool UseReadOnlyMemory { get; set; }

	/// <summary>The identity fields the <c>_bulk</c> responses report for each item. <see cref="Track.None"/> (the default) reports none, see <see cref="ReturnItemIdentity"/>.</summary>
	public Track ItemIdentity { get; private set; }

	/// <summary>
	/// Makes the <c>_bulk</c> responses report the given identity fields for every item, so generated ids and the concrete index behind an
	/// alias or data stream reach <c>DirectWriteAsync</c>, the response callbacks and <c>IngestAllAsync</c>.
	/// <c>options.ReturnItemIdentity(Track.Id | Track.Index)</c> requests both. Calls add up and cannot be undone.
	/// <para>Nothing is reported by default: every field makes responses larger and costs an allocation per item.
	/// Many workloads, for example logs written to a data stream, do not need them.</para>
	/// </summary>
	/// <returns>These options, for chaining.</returns>
	public IngestChannelOptionsBase<TEvent> ReturnItemIdentity(Track track)
	{
		if ((track & ~(Track.Id | Track.Index)) != 0) throw new ArgumentOutOfRangeException(nameof(track), track, "Unknown Track flags.");
		ItemIdentity |= track;
		return this;
	}

	private IJsonTypeInfoResolver? _serializerContext;

	/// <summary> The JsonSerializerContext to use for serialization. </summary>
	[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Fallback for user TEvent types not covered by a source-generated context")]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode", Justification = "Fallback for user TEvent types not covered by a source-generated context")]
	public JsonSerializerContext SerializerContext
	{
		set
		{
			_serializerContext = JsonTypeInfoResolver.Combine(
				new DefaultJsonTypeInfoResolver(),
				IngestSerializationContext.Default,
				ElasticsearchTransportSerializerContext.Default,
				value
			);
			_serializerOptions = new JsonSerializerOptions(IngestChannelStatics.SerializerOptions)
			{
				TypeInfoResolver = _serializerContext,
			};
		}
	}

	/// <summary> The JsonSerializerContexts to use for serialization. </summary>
	[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Fallback for user TEvent types not covered by a source-generated context")]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode", Justification = "Fallback for user TEvent types not covered by a source-generated context")]
	public JsonSerializerContext[] SerializerContexts
	{
		set
		{
			_serializerContext = JsonTypeInfoResolver.Combine(
				[
					new DefaultJsonTypeInfoResolver(),
					IngestSerializationContext.Default,
					ElasticsearchTransportSerializerContext.Default,
					.. value
				]
			);
			_serializerOptions = new JsonSerializerOptions(IngestChannelStatics.SerializerOptions)
			{
				TypeInfoResolver = _serializerContext,
			};
		}
	}

	private JsonSerializerOptions _serializerOptions = CreateDefaultSerializerOptions();

	[UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode", Justification = "Fallback for user TEvent types not covered by a source-generated context")]
	[UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode", Justification = "Fallback for user TEvent types not covered by a source-generated context")]
	private static JsonSerializerOptions CreateDefaultSerializerOptions() =>
		new(IngestChannelStatics.SerializerOptions)
		{
			TypeInfoResolver = JsonTypeInfoResolver.Combine(
				new DefaultJsonTypeInfoResolver(),
				IngestSerializationContext.Default,
				ElasticsearchTransportSerializerContext.Default
			),
		};

	internal JsonSerializerOptions SerializerOptions => _serializerOptions;
}
