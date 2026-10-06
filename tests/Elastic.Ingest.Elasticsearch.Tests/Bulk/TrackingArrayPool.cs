// Licensed to Elasticsearch B.V under one or more agreements.
// Elasticsearch B.V licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information

using System.Buffers;
using System.Collections.Generic;
using FluentAssertions;

namespace Elastic.Ingest.Elasticsearch.Tests.Bulk;

/// <summary>
/// An <see cref="ArrayPool{T}"/> that remembers every array it handed out, so tests can prove that every rented array
/// is returned exactly once, also when the code under test throws or is cancelled.
/// </summary>
public sealed class TrackingArrayPool<T> : ArrayPool<T>
{
	private readonly ArrayPool<T> _inner = Create();
	private readonly HashSet<T[]> _outstanding = new(ReferenceEqualityComparer.Instance);
	private readonly List<string> _violations = new();
	private int _rents;

	public int Rents => _rents;

	public int Outstanding { get { lock (_outstanding) return _outstanding.Count; } }

	public override T[] Rent(int minimumLength)
	{
		var array = _inner.Rent(minimumLength);
		lock (_outstanding)
		{
			_rents++;
			if (!_outstanding.Add(array)) _violations.Add("the pool handed out an array that was still outstanding");
		}
		return array;
	}

	public override void Return(T[] array, bool clearArray = false)
	{
		lock (_outstanding)
			if (!_outstanding.Remove(array))
			{
				_violations.Add("returned an array that was not rented from this pool, or was returned twice");
				return;
			}
		_inner.Return(array, clearArray);
	}

	/// <summary>Asserts nothing is outstanding and nothing was returned twice or from elsewhere.</summary>
	public void AssertBalanced()
	{
		lock (_outstanding)
		{
			_violations.Should().BeEmpty();
			_outstanding.Should().BeEmpty("every rented array must be returned");
		}
	}
}
