// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;
using Elastic.Ingest.Transport;
using static Elastic.Ingest.Elasticsearch.ElasticsearchChannelStatics;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// An abstract base class for both <see cref="DataStreamChannel{TEvent}"/> and <see cref="IndexChannel{TEvent}"/>
/// <para>Coordinates most of the sending to- and bootstrapping of Elasticsearch</para>
/// </summary>
public abstract partial class ElasticsearchChannelBase<TEvent, TChannelOptions>
	where TChannelOptions : ElasticsearchChannelOptionsBase<TEvent>
{
	/// <summary> TODO </summary>
	protected abstract (HeaderSerializationStrategy, BulkHeader?) EventIndexStrategy(TEvent @event);

	/// <summary>
	/// Asynchronously write the NDJSON request body for a page of <typeparamref name="TEvent"/> events to <see cref="Stream"/>.
	/// </summary>
	/// <param name="page">A page of <typeparamref name="TEvent"/> events.</param>
	/// <param name="stream">The target <see cref="Stream"/> for the request.</param>
	/// <param name="options">The <see cref="ElasticsearchChannelOptionsBase{TEvent}"/> for the channel where the request will be written.</param>
	/// <param name="ctx">The cancellation token to cancel operation.</param>
	/// <returns></returns>
	public async Task WriteBufferToStreamAsync(
		ArraySegment<TEvent> page, Stream stream, ElasticsearchChannelOptionsBase<TEvent> options, CancellationToken ctx = default)
	{
#if NETSTANDARD2_1_OR_GREATER
		var items = page;
#else
		// needs cast prior to netstandard2.0
		IReadOnlyList<TEvent> items = page;
#endif
		// for is okay on ArraySegment, foreach performs bad:
		// https://antao-almada.medium.com/how-to-use-span-t-and-memory-t-c0b126aae652
		// ReSharper disable once ForCanBeConvertedToForeach
		for (var i = 0; i < items.Count; i++)
		{
			var @event = items[i];
			if (@event == null) continue;

			var (op, header) = EventIndexStrategy(@event);
			switch (op)
			{
				case HeaderSerializationStrategy.IndexNoParams:
					await SerializePlainIndexHeaderAsync(stream, ctx).ConfigureAwait(false);
					break;
				case HeaderSerializationStrategy.CreateNoParams:
					await SerializePlainCreateHeaderAsync(stream, ctx).ConfigureAwait(false);
					break;
				case HeaderSerializationStrategy.Index:
				case HeaderSerializationStrategy.Create:
				case HeaderSerializationStrategy.Delete:
				case HeaderSerializationStrategy.Update:
					await SerializeHeaderAsync(stream, op, ref header, ctx).ConfigureAwait(false);
					break;
			}

			await stream.WriteAsync(LineFeed, 0, 1, ctx).ConfigureAwait(false);

			if (op == HeaderSerializationStrategy.Update)
				await stream.WriteAsync(DocUpdateHeaderStart, 0, DocUpdateHeaderStart.Length, ctx).ConfigureAwait(false);

			if (options.EventWriter?.WriteToStreamAsync != null)
				await options.EventWriter.WriteToStreamAsync(stream, @event, ctx).ConfigureAwait(false);
			else
				await JsonSerializer.SerializeAsync(stream, @event, SerializerOptions.GetTypeInfo(@event.GetType()), ctx)
					.ConfigureAwait(false);

			if (op == HeaderSerializationStrategy.Update)
				await stream.WriteAsync(DocUpdateHeaderEnd, 0, DocUpdateHeaderEnd.Length, ctx).ConfigureAwait(false);

			await stream.WriteAsync(LineFeed, 0, 1, ctx).ConfigureAwait(false);
		}
	}
}
