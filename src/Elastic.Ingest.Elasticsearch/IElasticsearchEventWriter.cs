// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Elastic.Ingest.Elasticsearch;

/// <summary>
/// Encapsulates logic to provide custom routines to write <typeparamref name="TEvent"/> to a receiving buffer.
/// <para>On NETSTANDARD2_1 this will use  </para>
/// </summary>
public interface IElasticsearchEventWriter<TEvent>
{
#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
	/// <summary>
	/// Provide a custom routine to write <typeparamref name="TEvent"/> to an ArrayBufferWriter{T}
	/// <para>This implementation is only called if <see cref="ElasticsearchChannelOptionsBase{TEvent}.UseReadOnlyMemory"/> is true, defaults to false</para>
	/// <para>Otherwise <see cref="WriteToStreamAsync"/> is called instead.</para>
	/// <para>If `null` <see cref="ElasticsearchChannelBase{TEvent,TChannelOptions}"/> will fallback to its own internal implementation.</para>
	/// </summary>
	Action<ArrayBufferWriter<byte>, TEvent>? WriteToArrayBuffer { get; set; }
#endif

	/// <summary>
	/// Provide a custom routine to write <typeparamref name="TEvent"/> to a <see cref="Stream"/> asynchronously
	/// <para>If `null` <see cref="ElasticsearchChannelBase{TEvent,TChannelOptions}"/> will fallback to its internal implementation.</para>
	/// </summary>
	Func<Stream, TEvent, CancellationToken, Task>? WriteToStreamAsync { get; set; }

}
