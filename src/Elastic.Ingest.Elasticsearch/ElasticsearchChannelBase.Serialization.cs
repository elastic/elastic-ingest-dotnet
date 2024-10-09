// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Elastic.Ingest.Elasticsearch.DataStreams;
using Elastic.Ingest.Elasticsearch.Indices;
using Elastic.Ingest.Elasticsearch.Serialization;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// An abstract base class for both <see cref="DataStreamChannel{TEvent}"/> and <see cref="IndexChannel{TEvent}"/>
/// <para>Coordinates most of the sending to- and bootstrapping of Elasticsearch</para>
/// </summary>
public abstract partial class ElasticsearchChannelBase<TEvent, TChannelOptions>
	where TChannelOptions : ElasticsearchChannelOptionsBase<TEvent>
{
	private static ReadOnlySpan<byte> PlainIndexBytesSpan => """
		{"index":{}}

		"""u8;

	private static ReadOnlySpan<byte> PlainCreateBytesSpan => """
		{"create":{}}

		"""u8;

#if NETSTANDARD

	private static byte[] PlainIndexBytes => PlainIndexBytesSpan.ToArray();

	private static byte[] PlainCreateBytes => PlainCreateBytesSpan.ToArray();
#endif

	private Task SerializeHeaderAsync(Stream stream, ref readonly BulkHeader? header, JsonSerializerOptions serializerOptions, CancellationToken ctx) =>
		throw new NotImplementedException();


#if NET8_0_OR_GREATER
	private static ValueTask SerializePlainIndexHeaderAsync(Stream stream, CancellationToken ctx = default)
	{
		stream.Write(PlainIndexBytesSpan);
		return ValueTask.CompletedTask;
	}
#else
	private static async ValueTask SerializePlainIndexHeaderAsync(Stream stream, CancellationToken ctx) =>
		await stream.WriteAsync(PlainIndexBytes, 0, PlainIndexBytes.Length, ctx).ConfigureAwait(false);
#endif

#if NET8_0_OR_GREATER
	private static ValueTask SerializePlainCreateHeaderAsync(Stream stream, CancellationToken ctx = default)
	{
		stream.Write(PlainCreateBytesSpan);
		return ValueTask.CompletedTask;
	}
#else
	private static async ValueTask SerializePlainCreateHeaderAsync(Stream stream, CancellationToken ctx) =>
		await stream.WriteAsync(PlainCreateBytes, 0, PlainCreateBytes.Length, ctx).ConfigureAwait(false);
#endif
}
