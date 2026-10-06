// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Text.Json.Serialization.Metadata;
using Elastic.Transport;

namespace Elastic.Ingest.Elasticsearch.Bulk;

/// <summary>Options for a <see cref="BulkSender{TItem,TBody}"/>. Does not depend on <c>Elastic.Mapping</c>.</summary>
/// <typeparam name="TItem">The item the action and body are derived from.</typeparam>
/// <typeparam name="TBody">The type serialized as the document.</typeparam>
public sealed class BulkSenderOptions<TItem, TBody>
{
	/// <summary>The transport to send the <c>_bulk</c> request over.</summary>
	public required ITransport Transport { get; init; }

	/// <summary>Produces the action line for each item.</summary>
	public required Func<TItem, BulkAction> Action { get; init; }

	/// <summary>Produces the document for each item. Not called for <see cref="BulkActionKind.Delete"/>.</summary>
	public required Func<TItem, TBody> Body { get; init; }

	/// <summary>The serialization metadata for <typeparamref name="TBody"/>, resolved once. Use a source generated context for AOT.</summary>
	public required JsonTypeInfo<TBody> BodyTypeInfo { get; init; }

	/// <summary>When set requests go to <c>{Target}/_bulk</c> and actions may omit their index.</summary>
	public string? Target { get; init; }

	/// <summary>
	/// Sets the <c>refresh</c> parameter of every <c>_bulk</c> request, so documents are searchable when the call returns.
	/// Defaults to <c>null</c>: the parameter is not sent and Elasticsearch refreshes on the index's own interval.
	/// </summary>
	public BulkRefresh? Refresh { get; init; }

	/// <summary>
	/// The client side timeout of every <c>_bulk</c> request (<see cref="IRequestConfiguration.RequestTimeout"/>), layered on top of the transport's own configuration.
	/// Defaults to <c>null</c>: the transport's request timeout applies.
	/// <para>This is not Elasticsearch's server side <c>timeout</c> parameter, which limits how long the cluster waits for active shards or mapping updates.</para>
	/// </summary>
	public TimeSpan? RequestTimeout { get; init; }

	/// <summary>The identity fields responses report for each item. <see cref="Bulk.Track.None"/> (the default) reports none, see <see cref="ReturnItemIdentity"/>.</summary>
	public Track ItemIdentity { get; private set; }

	/// <summary>
	/// Makes responses report the given identity fields for every item, applied to every request the sender issues.
	/// <c>options.ReturnItemIdentity(Track.Id | Track.Index)</c> requests both. Calls add up and cannot be undone.
	/// <para><see cref="Track.Id"/> is the only way to learn an id Elasticsearch generated (an <c>index</c> or <c>create</c> action without an id),
	/// <see cref="Track.Index"/> the only way to learn the concrete index behind an alias (<see cref="BulkAction.WithRequireAlias"/>) or a data stream.</para>
	/// <para>Each field costs: an item that carries an id is its own object plus the id string, and the response gets larger.
	/// Request only what you use. An index name is reused across the items of a response, so <see cref="Track.Index"/> is the cheaper one.</para>
	/// </summary>
	/// <returns>These options, for chaining.</returns>
	public BulkSenderOptions<TItem, TBody> ReturnItemIdentity(Track track)
	{
		if ((track & ~(Track.Id | Track.Index)) != 0) throw new ArgumentOutOfRangeException(nameof(track), track, "Unknown Track flags.");
		ItemIdentity |= track;
		return this;
	}

	/// <summary>
	/// When <c>true</c> (the default) documents are serialized with the same <see cref="System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault"/>
	/// default that channels use, so output is identical to a channel. This overrides a <c>DefaultIgnoreCondition</c> set on the
	/// serializer context (per property <c>[JsonIgnore(Condition = ...)]</c> attributes still apply).
	/// Set to <c>false</c> to serialize exactly as <see cref="BodyTypeInfo"/> is configured.
	/// </summary>
	public bool ApplyLibrarySerializerDefaults { get; init; } = true;

	/// <summary>The retry policy, defaults to <see cref="BulkRetryPolicy.None"/>.</summary>
	public BulkRetryPolicy Retry { get; init; } = BulkRetryPolicy.None;
}
