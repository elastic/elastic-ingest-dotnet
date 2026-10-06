// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Buffers;

// ThrowIfNegative helpers are not available on netstandard
#pragma warning disable CA1512

namespace Elastic.Ingest.Elasticsearch.Bulk;

/// <summary>An <see cref="IBufferWriter{T}"/> over an <see cref="ArrayPool{T}"/> rented array. Must be disposed to return the array.</summary>
internal sealed class PooledByteBufferWriter : IBufferWriter<byte>, IDisposable
{
	private const int MinimumGrowth = 4096;
	private const int MaxArrayLength = 0x7FFFFFC7;
	private byte[] _buffer;
	private int _written;

	public PooledByteBufferWriter(int initialCapacity = 16 * 1024) =>
		_buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, 256));

	public int WrittenCount => _written;

	public int Capacity => _buffer.Length;

	/// <summary>The backing array, valid up to <see cref="WrittenCount"/>. Only valid until the next write.</summary>
	public byte[] RawArray => _buffer;

	public ReadOnlyMemory<byte> WrittenMemory => new(_buffer, 0, _written);

	public Span<byte> WrittenSpan => new(_buffer, 0, _written);

	public void Advance(int count)
	{
		if (count < 0 || _written > _buffer.Length - count)
			throw new ArgumentOutOfRangeException(nameof(count));
		_written += count;
	}

	public Memory<byte> GetMemory(int sizeHint = 0)
	{
		EnsureCapacity(sizeHint);
		return _buffer.AsMemory(_written);
	}

	public Span<byte> GetSpan(int sizeHint = 0)
	{
		EnsureCapacity(sizeHint);
		return _buffer.AsSpan(_written);
	}

	/// <summary>Truncates (or logically resets) the written count. Only valid for <paramref name="count"/> &lt;= <see cref="WrittenCount"/>.</summary>
	public void Truncate(int count)
	{
		if (count < 0 || count > _written) throw new ArgumentOutOfRangeException(nameof(count));
		_written = count;
	}

	public void Write(ReadOnlySpan<byte> value)
	{
		value.CopyTo(GetSpan(value.Length));
		_written += value.Length;
	}

	private void EnsureCapacity(int sizeHint)
	{
		if (sizeHint < 0) throw new ArgumentOutOfRangeException(nameof(sizeHint));
		if (sizeHint == 0) sizeHint = 1;
		var available = _buffer.Length - _written;
		if (sizeHint <= available) return;

		var newSize = (int)Math.Min(Math.Max((long)_buffer.Length * 2, (long)_written + sizeHint + MinimumGrowth), MaxArrayLength);
		if (newSize - _written < sizeHint) throw new InvalidOperationException("Bulk request body exceeds the maximum buffer size.");
		var next = ArrayPool<byte>.Shared.Rent(newSize);
		Buffer.BlockCopy(_buffer, 0, next, 0, _written);
		ArrayPool<byte>.Shared.Return(_buffer);
		_buffer = next;
	}

	public void Dispose()
	{
		var b = _buffer;
		_buffer = Array.Empty<byte>();
		_written = 0;
		if (b.Length > 0) ArrayPool<byte>.Shared.Return(b);
	}
}
