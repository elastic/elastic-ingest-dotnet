// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Elastic.Ingest.Elasticsearch.Serialization;

/// <summary>
/// A write-only <see cref="Stream"/> that discards all bytes and only accumulates a byte count.
/// Used to measure the serialized size of an event without allocating a buffer.
/// </summary>
internal sealed class CountingStream : Stream
{
	private long _bytesWritten;

	/// <summary> Total bytes written since the last <see cref="Reset"/>. </summary>
	public long BytesWritten => _bytesWritten;

	/// <summary> Resets the byte counter to zero for reuse across measurements. </summary>
	public void Reset() => _bytesWritten = 0;

	public override bool CanRead => false;
	public override bool CanSeek => false;
	public override bool CanWrite => true;
	public override long Length => _bytesWritten;
	public override long Position { get => _bytesWritten; set => throw new NotSupportedException(); }

	public override void Write(byte[] buffer, int offset, int count) => _bytesWritten += count;

	public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
	{
		_bytesWritten += count;
		return Task.CompletedTask;
	}

#if NETSTANDARD2_1_OR_GREATER || NET8_0_OR_GREATER
	public override void Write(ReadOnlySpan<byte> buffer) => _bytesWritten += buffer.Length;

	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
	{
		_bytesWritten += buffer.Length;
		return default;
	}
#endif

	public override void Flush() { }
	public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

	public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
}
