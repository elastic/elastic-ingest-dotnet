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

	/// <summary>The retry policy, defaults to <see cref="BulkRetryPolicy.None"/>.</summary>
	public BulkRetryPolicy Retry { get; init; } = BulkRetryPolicy.None;
}
