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
	/// <summary>
	/// Provide a custom routine to write <typeparamref name="TEvent"/> to a <see cref="Stream"/> asynchronously
	/// <para>If `null` <see cref="ElasticsearchChannelBase{TEvent,TChannelOptions}"/> will fallback to its internal implementation.</para>
	/// </summary>
	Func<Stream, TEvent, CancellationToken, Task>? WriteToStreamAsync { get; set; }

}
