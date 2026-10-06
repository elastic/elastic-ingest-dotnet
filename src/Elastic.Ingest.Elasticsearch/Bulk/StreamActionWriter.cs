// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static Elastic.Ingest.Elasticsearch.IngestChannelStatics;

// the array overload of WriteAsync is the only one available on netstandard2.0
#pragma warning disable CA1835

namespace Elastic.Ingest.Elasticsearch.Bulk;

/// <summary>
/// Writes the action line and wrappers of a bulk operation to a <see cref="Stream"/> through one small reused scratch buffer,
/// so streaming exports share <see cref="BulkNdjsonWriter"/> without allocating per event.
/// </summary>
internal sealed class StreamActionWriter : IDisposable
{
	private readonly PooledByteBufferWriter _scratch = new(1024);
	private readonly Utf8JsonWriter _writer;

	public StreamActionWriter() => _writer = new Utf8JsonWriter(_scratch, WriterOptions);

	/// <summary>Writes the action line (and wrapper start), returns <c>true</c> if a document must follow.</summary>
	public async Task<bool> WritePrefixAsync(Stream stream, BulkAction action, CancellationToken ctx)
	{
		_scratch.Truncate(0);
		_writer.Reset(_scratch);
		var hasBody = BulkNdjsonWriter.WritePrefix(_scratch, _writer, in action);
		await stream.WriteAsync(_scratch.RawArray, 0, _scratch.WrittenCount, ctx).ConfigureAwait(false);
		return hasBody;
	}

	/// <summary>Writes the wrapper end and trailing line feed.</summary>
	public Task WriteSuffixAsync(Stream stream, BulkAction action, CancellationToken ctx)
	{
		_scratch.Truncate(0);
		BulkNdjsonWriter.WriteSuffix(_scratch, in action);
		return stream.WriteAsync(_scratch.RawArray, 0, _scratch.WrittenCount, ctx);
	}

	public void Dispose()
	{
		_writer.Dispose();
		_scratch.Dispose();
	}
}
