// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CsCheck;
using Elastic.Ingest.Elasticsearch.Bulk;
using FluentAssertions;
using TUnit.Core;

namespace Elastic.Ingest.Elasticsearch.Tests.Bulk;

/// <summary>Direct tests of the internal pooled buffers (visible through InternalsVisibleTo).</summary>
public class PooledBufferTests
{
	private static readonly Gen<byte[]> Chunk = Gen.Int[0, 600].SelectMany(n => Gen.Byte.Array[n]);

	// 0 = GetSpan + copy + Advance, 1 = GetMemory + copy + Advance, 2 = Write(span), 3 = truncate to a fraction
	private static readonly Gen<(int Op, byte[] Data, int Hint, int Fraction)> Op =
		Gen.Select(Gen.Int[0, 3], Chunk, Gen.Int[0, 70_000], Gen.Int[0, 100]);

	[Test]
	public async Task WrittenBytesMatchAMemoryStreamModelAndTheArrayIsReturned()
	{
		await Op.List[0, 40].SampleAsync(ops =>
		{
			var pool = new TrackingArrayPool<byte>();
			var model = new List<byte>();
			var writer = new PooledByteBufferWriter(initialCapacity: 256, pool);
			foreach (var (op, data, hint, fraction) in ops)
			{
				switch (op)
				{
					case 0:
						data.AsSpan().CopyTo(writer.GetSpan(Math.Max(hint % 1000, data.Length)));
						writer.Advance(data.Length);
						model.AddRange(data);
						break;
					case 1:
						data.AsMemory().CopyTo(writer.GetMemory(data.Length));
						writer.Advance(data.Length);
						model.AddRange(data);
						break;
					case 2:
						writer.Write(data);
						model.AddRange(data);
						break;
					default:
						var keep = model.Count * fraction / 100;
						writer.Truncate(keep);
						model.RemoveRange(keep, model.Count - keep);
						break;
				}
				// a large hint must grow the buffer without losing what was written (and without being written)
				if (hint > 60_000)
				{
					writer.GetSpan(hint).Length.Should().BeGreaterThanOrEqualTo(hint);
					writer.WrittenSpan.ToArray().Should().Equal(model);
				}
				writer.WrittenCount.Should().Be(model.Count);
				pool.Outstanding.Should().Be(1, "growing returns the previous array immediately");
			}
			writer.WrittenSpan.ToArray().Should().Equal(model);
			writer.WrittenMemory.ToArray().Should().Equal(model);

			writer.Dispose();
			writer.Dispose();
			pool.AssertBalanced();
			return Task.CompletedTask;
		}, iter: 300);
	}

	[Test]
	public void InvalidArgumentsAreRejectedAndDoNotCorruptTheBuffer()
	{
		using var writer = new PooledByteBufferWriter(256);
		writer.Write(new byte[] { 1, 2, 3 });

		((Action)(() => writer.Advance(-1))).Should().Throw<ArgumentOutOfRangeException>();
		((Action)(() => writer.Advance(writer.Capacity))).Should().Throw<ArgumentOutOfRangeException>("cannot advance past what was handed out");
		((Action)(() => writer.GetSpan(-1))).Should().Throw<ArgumentOutOfRangeException>();
		((Action)(() => writer.GetMemory(-1))).Should().Throw<ArgumentOutOfRangeException>();
		((Action)(() => writer.Truncate(4))).Should().Throw<ArgumentOutOfRangeException>();
		((Action)(() => writer.Truncate(-1))).Should().Throw<ArgumentOutOfRangeException>();

		writer.WrittenSpan.ToArray().Should().Equal(1, 2, 3);
	}

	[Test]
	public void ASizeHintThatCanNeverFitThrowsInsteadOfAllocatingGarbage()
	{
		using var writer = new PooledByteBufferWriter(256);
		Action act = () => { _ = writer.GetSpan(int.MaxValue).Length; };
		act.Should().Throw<Exception>();
	}

	[Test]
	public async Task CompactionKeepsExactlyTheSelectedOperationsEvenWhenChained()
	{
		var gen = Chunk.List[0, 70].SelectMany(ops =>
			Gen.Bool.Array[ops.Count].SelectMany(first => Gen.Bool.Array[first.Count(k => k)].Select(second => (ops, first, second))));

		await gen.SampleAsync(x =>
		{
			var (ops, first, second) = x;
			var bytePool = new TrackingArrayPool<byte>();
			var intPool = new TrackingArrayPool<int>();
			using (var buffer = new BulkRequestBuffer(2, bytePool, intPool))
			{
				foreach (var op in ops)
				{
					buffer.Body.Write(op);
					buffer.CompleteOperation();
				}
				buffer.Count.Should().Be(ops.Count);

				var afterFirst = ops.Where((_, i) => first[i]).ToList();
				buffer.Compact(first.Select((k, i) => (k, i)).Where(t => t.k).Select(t => t.i).ToArray());
				buffer.Count.Should().Be(afterFirst.Count);
				buffer.Body.WrittenSpan.ToArray().Should().Equal(afterFirst.SelectMany(b => b));

				var afterSecond = afterFirst.Where((_, i) => second[i]).ToList();
				buffer.Compact(second.Select((k, i) => (k, i)).Where(t => t.k).Select(t => t.i).ToArray());
				buffer.Count.Should().Be(afterSecond.Count);
				buffer.Body.WrittenSpan.ToArray().Should().Equal(afterSecond.SelectMany(b => b));

				// operations can still be appended and compacted after a compaction
				buffer.Body.Write(new byte[] { 9, 9 });
				buffer.CompleteOperation();
				buffer.Compact(new[] { buffer.Count - 1 });
				buffer.Body.WrittenSpan.ToArray().Should().Equal(9, 9);
			}
			bytePool.AssertBalanced();
			intPool.AssertBalanced();
			return Task.CompletedTask;
		}, iter: 300);
	}

	[Test]
	public void CompactingToNothingAndKeepingEverythingAreNoOps()
	{
		var bytePool = new TrackingArrayPool<byte>();
		var intPool = new TrackingArrayPool<int>();
		using (var buffer = new BulkRequestBuffer(3, bytePool, intPool))
		{
			foreach (var op in new[] { new byte[] { 1 }, new byte[] { 2, 2 }, new byte[] { 3, 3, 3 } })
			{
				buffer.Body.Write(op);
				buffer.CompleteOperation();
			}
			buffer.Compact(new[] { 0, 1, 2 });
			buffer.Body.WrittenSpan.ToArray().Should().Equal(1, 2, 2, 3, 3, 3);

			buffer.Compact(ReadOnlySpan<int>.Empty);
			buffer.Count.Should().Be(0);
			buffer.Body.WrittenCount.Should().Be(0);
		}
		bytePool.AssertBalanced();
		intPool.AssertBalanced();
	}

	[Test]
	public void ReleasedWritersAreReusedOnThisThreadAndNeverLeakBytesIntoAnotherBuffer()
	{
		using var a = new BulkRequestBuffer(1);
		using var b = new BulkRequestBuffer(1);

		var w1 = a.RentWriter();
		var nested = b.RentWriter();
		nested.Should().NotBeSameAs(w1, "a writer in use is never handed out twice");
		b.ReleaseWriter(nested);
		a.ReleaseWriter(w1);

		var reused = a.RentWriter();
		reused.WriteStartObject();
		reused.WriteEndObject();
		reused.Flush();
		a.ReleaseWriter(reused);
		var aBytes = a.Body.WrittenSpan.ToArray();
		aBytes.Should().Equal("{}"u8.ToArray());

		var other = b.RentWriter();
		other.WriteStartArray();
		other.WriteEndArray();
		other.Flush();
		b.ReleaseWriter(other);

		b.Body.WrittenSpan.ToArray().Should().Equal("[]"u8.ToArray());
		a.Body.WrittenSpan.ToArray().Should().Equal(aBytes, "the second buffer must not touch the first one");
	}

	[Test]
	public void DisposingWhileHoldingNoWritesReturnsEverythingAndIsIdempotent()
	{
		var bytePool = new TrackingArrayPool<byte>();
		var intPool = new TrackingArrayPool<int>();
		var buffer = new BulkRequestBuffer(10, bytePool, intPool);
		buffer.Dispose();
		buffer.Dispose();
		bytePool.AssertBalanced();
		intPool.AssertBalanced();
	}
}
