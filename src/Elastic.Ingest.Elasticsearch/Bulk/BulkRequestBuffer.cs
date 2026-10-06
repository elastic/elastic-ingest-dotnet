// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Buffers;
using System.Text.Json;
using static Elastic.Ingest.Elasticsearch.IngestChannelStatics;

namespace Elastic.Ingest.Elasticsearch.Bulk;

/// <summary>
/// A pooled NDJSON request body plus the end offset of every operation in it, so a subset of operations can be
/// compacted in place and re-sent without serializing anything again.
/// </summary>
internal sealed class BulkRequestBuffer : IDisposable
{
	[ThreadStatic]
	private static Utf8JsonWriter? _cachedWriter;

	private readonly ArrayPool<int> _intPool;
	private int[] _ends;

	public BulkRequestBuffer(int expectedItems, ArrayPool<byte>? bytePool = null, ArrayPool<int>? intPool = null)
	{
		_intPool = intPool ?? ArrayPool<int>.Shared;
		Body = new PooledByteBufferWriter(Math.Max(expectedItems, 1) * 256, bytePool);
		_ends = _intPool.Rent(Math.Max(expectedItems, 16));
	}

	public PooledByteBufferWriter Body { get; }

	public int Count { get; private set; }

	/// <summary>Takes the thread's cached writer and points it at the body. Always pair with <see cref="ReleaseWriter"/>.</summary>
	public Utf8JsonWriter RentWriter()
	{
		var writer = _cachedWriter;
		_cachedWriter = null;
		if (writer is null) return new Utf8JsonWriter(Body, WriterOptions);
		writer.Reset(Body);
		return writer;
	}

	public void ReleaseWriter(Utf8JsonWriter writer)
	{
		writer.Reset();
		_cachedWriter = writer;
	}

	/// <summary>Marks the end of one operation.</summary>
	public void CompleteOperation()
	{
		if (Count == _ends.Length)
		{
			var next = _intPool.Rent(_ends.Length * 2);
			Array.Copy(_ends, next, Count);
			_intPool.Return(_ends);
			_ends = next;
		}
		_ends[Count++] = Body.WrittenCount;
	}

	/// <summary>
	/// Keeps only the operations at <paramref name="keep"/> (ascending positions) moving their bytes forward in place.
	/// </summary>
	public void Compact(ReadOnlySpan<int> keep)
	{
		var span = Body.WrittenSpan;
		var write = 0;
		var j = 0;
		var previousEnd = 0;
		var k = 0;
		var next = keep.Length > 0 ? keep[0] : -1;
		for (var i = 0; i < Count && j < keep.Length; i++)
		{
			var end = _ends[i];
			if (i == next)
			{
				var length = end - previousEnd;
				if (previousEnd != write)
					span.Slice(previousEnd, length).CopyTo(span.Slice(write, length));
				write += length;
				_ends[j++] = write;
				k++;
				next = k < keep.Length ? keep[k] : -1;
			}
			previousEnd = end;
		}
		Body.Truncate(write);
		Count = j;
	}

	public void Dispose()
	{
		Body.Dispose();
		var ends = _ends;
		_ends = Array.Empty<int>();
		if (ends.Length > 0) _intPool.Return(ends);
	}
}
